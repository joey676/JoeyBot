using System;
using RLBotDotNet;
using rlbot.flat;

namespace RLBotCSharpExample
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class JoeyBot : Bot
    {
        // We want the constructor for ExampleBot to extend from Bot, but we don't want to add anything to it.
        public JoeyBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) {

           
        }
        static int MinimumSteeringAngleDegrees = 10;
        static double MinimumSteeringAngleRadians = DegreesToRadians(MinimumSteeringAngleDegrees);
        static int DistanceToJump = 300;

        static bool Jump1 = false;
        static bool firstLoopComplete = false;
        static double DoubleJumpWaitTime = 10000;
        long Jump1Time = 0;
        bool Jump2 = false;

        public override Controller GetOutput(GameTickPacket gameTickPacket)
        {
            // This controller object will be returned at the end of the method.
            // This controller will contain all the inputs that we want the bot to perform.
            Controller controller = new Controller();

            // Wrap gameTickPacket retrieving in a try-catch so that the bot doesn't crash whenever a value isn't present.
            // A value may not be present if it was not sent.
            // These are nullables so trying to get them when they're null will cause errors, therefore we wrap in try-catch.
            try
            {
                // Store the required data from the gameTickPacket.
                Vector3 ballLocation = gameTickPacket.Ball.Value.Physics.Value.Location.Value;
                Vector3 carLocation = gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value;
                Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;

                int ownGoalY = index == 0 ? -5000 : 5000;
                int oppGoalY = index == 0 ? 5000 : -5000;


                // Calculate to get the angle from the front of the bot's car to the target.
                //If car is in front of ball aim for goal, else aim for ball

                double botToTargetAngle;
                double botToBallAngle;
                double botToOppGoalAngle;

                botToBallAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);

                if ((index == 0 && carLocation.Y < ballLocation.Y) || (index == 1 && carLocation.Y > ballLocation.Y))
                {
                    botToTargetAngle = botToBallAngle;
                }
                else if (index == 0)
                {
                    botToTargetAngle = Math.Atan2(-5000 - carLocation.Y, 0 - carLocation.X);
                }
                else
                {
                    botToTargetAngle = Math.Atan2(5000 - carLocation.Y, 0 - carLocation.X);
                }

                double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
                double botFrontToBallAngle = botToBallAngle - carRotation.Yaw;


                // Correct the angle
                botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);
                botFrontToBallAngle = CorrectAngle(botFrontToBallAngle);

                if (Math.Sqrt(carLocation.Y * carLocation.Y) > 4950 && Math.Sqrt(carLocation.X * carLocation.X) > 1000 && (botFrontToTargetAngle > MinimumSteeringAngleRadians * 2 || botFrontToTargetAngle < MinimumSteeringAngleRadians * -2)) controller.Handbrake = true;
                if (botFrontToTargetAngle > DegreesToRadians(90) || botFrontToTargetAngle < DegreesToRadians(-90)) controller.Handbrake = true;


                var DistanceToBall = Get2DDistance(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);

                bool guidingBall = false;
                // Decide which way to steer in order to get to the ball.

                if (DistanceToBall <= 300 && botFrontToTargetAngle < MinimumSteeringAngleRadians / 2 && ballLocation.Z < 200)
                {
                    guidingBall = true;
                    if (ballLocation.Y < 0) controller.Steer = 0.05f;
                    else controller.Steer = -0.05f;

                }

                else
                {
                    if (botFrontToTargetAngle > 0)
                        if (botFrontToTargetAngle > MinimumSteeringAngleRadians)
                            if (botFrontToTargetAngle > DegreesToRadians(90))
                                controller.Steer = 1f;
                            else
                                controller.Steer = 0.75f;
                        else controller.Steer = 0;
                    else
                                if (botFrontToTargetAngle < MinimumSteeringAngleRadians * -1)
                        if (botFrontToTargetAngle < DegreesToRadians(-90))
                            controller.Steer = -1f;
                        else
                            controller.Steer = -0.75f;
                    else controller.Steer = 0;

                }


                if (ballLocation.X == 0 && ballLocation.Y == 0 || DistanceToBall > 2000 && !controller.Handbrake)
                {
                    controller.Boost = true;
                }



                //Between First and Second Jump
                if (Jump1 && !Jump2)
                {
                    controller.Jump = false;
                    if (Jump1Time + DoubleJumpWaitTime <= DateTime.Now.Ticks && firstLoopComplete)
                    {
                        //do second jump
                        controller.Jump = true;
                        Jump2 = true;
                        controller.Pitch = -1;
                    }
                    controller.Throttle = 0;
                    firstLoopComplete = true;
                }
                else controller.Throttle = 1;

                //After second jump
                if ((Jump1 && Jump2) || carLocation.Z == 0)
                {
                    controller.Jump = false;
                    Jump1 = false;
                    Jump2 = false;
                    firstLoopComplete = false;
                    Jump1Time = 0;

                }
                else controller.Pitch = -1;

                //First Jump
                if (DistanceToBall < DistanceToJump && !Jump1 && Math.Sqrt(botFrontToBallAngle * botFrontToBallAngle) < DegreesToRadians(10) && ballLocation.Z < 600 && !guidingBall)
                {
                    controller.Jump = true;
                    Jump1 = true;
                    Jump1Time = DateTime.Now.Ticks;
                }

                if (botFrontToTargetAngle > 90 || botFrontToTargetAngle < -90)
                    controller.Throttle = 0.5f;
                else
                    controller.Throttle = 1;

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }

           

            

            
           
            return controller;
        }

        private static double CorrectAngle(double botFrontToTargetAngle)
        {
            if (botFrontToTargetAngle < -Math.PI)
                botFrontToTargetAngle += 2 * Math.PI;
            if (botFrontToTargetAngle > Math.PI)
                botFrontToTargetAngle -= 2 * Math.PI;
            return botFrontToTargetAngle;
        }

        public double Get2DDistance(double x1, double x2, double y1, double y2)
        {
            return Math.Sqrt(Math.Pow((x2 - x1), 2) + Math.Pow((y2 - y1), 2));
        }

        public static double DegreesToRadians(double degerees)
        {
            return degerees * Math.PI / 180;
        }
    }
}
