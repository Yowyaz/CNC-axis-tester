using System;
using System.IO;

namespace CNC_axis_tester
{
	internal class TestCreator
	{
		#region GCodes

		private const string gFeed = "G1";
		private const string gFeedSpeed = "F";
		private const string gRapid = "G0";

		#endregion GCodes

		private decimal currentPosition;

		#region distances

		private decimal fullStepDistance;
		private decimal microStepDistance;
		private decimal maxVelocityDistance;

		#endregion distances

		#region places

		/// <summary>
		/// where the program will stop to get measured by the dial indicator. Also where the program starts from.
		/// </summary>
		private decimal endPoint;

		/// <summary>
		/// the upper limit of where we'll move around
		/// </summary>
		private decimal maxPlayground;

		/// <summary>
		/// the lower limit of where we'll move around
		/// </summary>
		private decimal minPlayground;

		#endregion places

		private decimal lastDirection;
		private Random random;

		/// <summary>
		/// axis that we are testing
		/// </summary>
		private string testingAxis;

		private int precision;

		public TestCreator()
		{
			// all units in inches
			decimal stepsPerRevolution = 200;
			decimal gearReduction = 3;
			decimal pinionCircumference = (decimal)Math.PI;
			decimal numMicrosteps = 10;

			this.fullStepDistance = 1 / (stepsPerRevolution * gearReduction / pinionCircumference);
			this.microStepDistance = this.fullStepDistance / numMicrosteps;

			// inches per second
			decimal maxVelocity = 6.67M;

			// inches per second^2
			decimal maxAcceleration = 20;
			this.maxVelocityDistance = MaxVelocityDistance(maxVelocity, maxAcceleration);

			// things that could change
			this.testingAxis = "X";
			this.minPlayground = 0;
			this.maxPlayground = 8;
			this.endPoint = 9;

			this.currentPosition = 0;
			this.random = new Random();

			// start out heading from end point to the playground
			this.lastDirection = this.maxPlayground - this.endPoint;

			this.precision = 4;
		}

		private static void Main(string[] args)
		{
			for (int i = 3; i < 11; i++)
			{
				int jogs = 1 << i;
				TestCreator tc = new TestCreator();
				tc.createProgram(string.Format("xtest{0,4}.ngc", jogs), jogs);
			}

			// The hypothesis is that we're commanding the CNC to stop at microsteps and it can't so it slides over to the
			// nearest full step (or 2 full steps over from what I've seen). So the goal is to command it to move to microstep
			// locations, not full step locations.

			// I assume that it helps if we have some room to accelerate & decelerate, making it more likely to miss a microstep.
			// The bridge picture was less than 3 inches in the Y dim, so I don't think I need a full 5 inches of
			// acceleration/deceleration space.

			// Gecko step motor basics When lateral torque sufficient enough to overcome the holding torque is applied to a step
			// motor, the shaft will jump to the next stable location which is four full steps ahead or behind the original one,
			// depending on the direction of the lateral torque. Peak restoring torque occurs a full step ahead or behind the
			// original location, beyond which it weakens and reverses at the two full step position to attract the shaft to a
			// four full step location ahead or behind the original one.
		}

		public void createProgram(string outputPath, int jogs)
		{
			using (StreamWriter sw = File.CreateText(outputPath))
			{
				preamble(sw);

				for (int i = 0; i < jogs; i++)
				{
					this.jogOnce(sw);
				}

				this.rapidTo(sw, this.maxPlayground);
				this.feedTo(sw, this.endPoint, 50);
				this.epilogue(sw);
			}
		}

		private void preamble(StreamWriter sw)
		{
			var stuff = new[] {"( sane defaults )",
				"( XY plane, inch mode, cancel diameter compensation, cancel length offset, coordinate system 1, Cancel Canned Cycle, Absolute distance mode, feed/minute mode )",
				"G17 G20 G40 G49 G54 G80 G90 G94",
				"( spindle stop, coolant off )",
				"M5 M9"};
			foreach (var item in stuff)
			{
				sw.WriteLine(item);
			}
		}

		private void epilogue(StreamWriter sw)
		{
			sw.WriteLine("M2");
		}

		private void jogOnce(StreamWriter sw)
		{
			var movement = this.chooseDistance(this.chooseDirection());
			movement = Math.Round(movement, this.precision);
			var newPos = this.currentPosition + movement;

			this.rapidTo(sw, newPos);
			this.currentPosition = newPos;
		}

		private string formatPosition(decimal pos)
		{
			var formatString = "{0:F" + this.precision + "}";
			return string.Format(formatString, pos);
		}

		private string getMovement(string type, decimal destination)
		{
			return string.Format("{0} {1}{2}", type, this.testingAxis, this.formatPosition(destination));
		}

		private void rapidTo(StreamWriter sw, decimal destination)
		{
			sw.WriteLine(getMovement(gRapid, destination));
		}

		private void feedTo(StreamWriter sw, decimal destination, decimal speed)
		{
			string speedStr = string.Format("{0}{1:F1}", gFeedSpeed, speed);
			sw.WriteLine(string.Format("{0} {1}", getMovement(gFeed, destination), speedStr));
		}

		/// <summary>
		/// figures out in which direction the next jog should go
		/// </summary>
		/// <returns>-1 if we should jog in negative axis direction, +1 to move in positive axis direction</returns>
		private decimal chooseDirection()
		{
			this.lastDirection = -this.lastDirection;
			return this.lastDirection;
		}

		/// <summary>
		/// Determines the distance of the next jog.
		/// </summary>
		/// <returns></returns>
		private decimal chooseDistance(decimal direction)
		{
			// I think the general strategy should be to move at least 2 full steps away, and at most move MaxVelocityDistance.
			// We should not be moving a multiple of a full step distance, we should move to a microstep. BTW a 10 microstep drive
			// with 1.8 degrees per full step moves 0.18 degrees for each microstep.
			decimal maxDistance;
			if (direction < 0)
			{
				maxDistance = Math.Abs(this.minPlayground - this.currentPosition);
			}
			else
			{
				maxDistance = Math.Abs(this.maxPlayground - this.currentPosition);
			}
			maxDistance = Math.Max(maxDistance, this.maxVelocityDistance);

			int minFullSteps = 2;
			int maxFullSteps = (int)Math.Floor(maxDistance / this.fullStepDistance);
			decimal fullSteps = this.random.Next(minFullSteps, maxFullSteps + 1);

			int microSteps = this.random.Next(1, 10);

			return direction * fullSteps * this.fullStepDistance + microSteps * this.microStepDistance;
		}

		/// <summary>
		/// Distance needed to accelerate to maximum velocity and then decelerate to 0.
		/// </summary>
		private decimal MaxVelocityDistance(decimal maxVel, decimal maxAccel)
		{
			return maxVel * maxVel / maxAccel;
		}
	}
}