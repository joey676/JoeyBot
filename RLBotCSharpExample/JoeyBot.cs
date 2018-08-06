using System;
using RLBotDotNet;
using rlbot.flat;

namespace JoeyBot
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class JoeyBot : Bot
    {
        // We want the constructor for ExampleBot to extend from Bot, but we don't want to add anything to it.
        public JoeyBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex) {

           
        }

        private const float smallSteering = 0.1f;
        private const float largeSteering = 0.7f;
        static int MinimumSteeringAngleDegrees = 10;
        static double MinimumSteeringAngleRadians = DegreesToRadians(MinimumSteeringAngleDegrees);
        static int DistanceToJump = 300;

        static bool Jump1 = false;
        static bool firstLoopComplete = false;
        static double DoubleJumpWaitTime = 10000;
        long Jump1Time = 0;
        bool Jump2 = false;
        
        private double LastPositionX;
        private double LastPositionY;
        private double LastBallPositionX;
        private double LastBallPositionY;
        private double LastBallPositionZ;

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
                bool defenceMode = false;
                // Store the required data from the gameTickPacket.
                Vector3 ballLocation = gameTickPacket.Ball.Value.Physics.Value.Location.Value;
                Vector3 carLocation = gameTickPacket.Players(this.index).Value.Physics.Value.Location.Value;
                
                Rotator carRotation = gameTickPacket.Players(this.index).Value.Physics.Value.Rotation.Value;
               Vector3 ballVelocity = gameTickPacket.Ball.Value.Physics.Value.Velocity.Value;
                Vector3 carVelocity = gameTickPacket.Players(this.index).Value.Physics.Value.Velocity.Value;
                int ownGoalY = team == 0 ? -5000 : 5000;
                int oppGoalY = team == 0 ? 5000 : -5000;


                // Calculate to get the angle from the front of the bot's car to the target.
                //If car is in front of ball aim for goal, else aim for ball

                double botToTargetAngle;
                double botToBallAngle;
                double botToOppGoalAngle;
                var DistanceToBall = Get2DDistance(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);
                botToBallAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
            

                //car is in goal
                if(carLocation.Y < -5000 || carLocation.Y > 5000)
                {
                    botToTargetAngle = Math.Atan2(-carLocation.Y, -carLocation.X);
                }
                //go for the ball
               else if ((team == 0 && carLocation.Y < ballLocation.Y) || (team == 1 && carLocation.Y > ballLocation.Y) || DistanceToBall < 1000)
                {
                    botToTargetAngle = botToBallAngle;
                }
                //defending when blue
                else if (team == 0)
                {
                    botToTargetAngle = Math.Atan2(-5000 - carLocation.Y, 0 - carLocation.X);
                    controller.Boost = true;
                    defenceMode = true;
                }
                //defending when orange
                else
                {
                    botToTargetAngle = Math.Atan2(5000 - carLocation.Y, 0 - carLocation.X);
                    controller.Boost = true;
                    defenceMode = true;
                }

                double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
                double botFrontToBallAngle = botToBallAngle - carRotation.Yaw;



                // Correct the angle
                botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);
                botFrontToBallAngle = CorrectAngle(botFrontToBallAngle);

                if (Math.Abs(carLocation.Y) < 4950 && Math.Abs(carLocation.X) > 1500 && (botFrontToTargetAngle > MinimumSteeringAngleRadians * 2 || botFrontToTargetAngle < MinimumSteeringAngleRadians * -2)) controller.Handbrake = true;
                if (botFrontToTargetAngle > DegreesToRadians(135) || botFrontToTargetAngle < DegreesToRadians(-135)) controller.Handbrake = true;

                if (carLocation.Z > 0) controller.Handbrake = false;
                

                bool guidingBall = false;
                // Decide which way to steer in order to get to the ball.

                if (DistanceToBall <= 300 && botFrontToTargetAngle < MinimumSteeringAngleRadians / 2 && ballLocation.Z < 200 && ballLocation.X != 0 && ballLocation.Y != 0)
                {
                    guidingBall = true;
                    var complete = false;

                    var ballTrajectoryX = ballLocation.X;
                    var ballTrajectoryY = ballLocation.Y;
                    
                    while (!complete)
                    {
                        if (ballTrajectoryY > 5000)
                        {
                            if (oppGoalY == 5000)
                            {
                                if (ballLocation.X < 700 && ballLocation.X > -700)
                                    complete = true;
                                else
                                {
                                    if (ballLocation.X < -700)
                                        controller.Steer = -largeSteering;
                                    else controller.Steer = largeSteering;
                                    complete = true;
                                }
                            }
                            else
                            {
                                controller.Steer = 1;
                                complete = true;
                            }
                        }
                        else if (ballTrajectoryY < -5000)
                        {

                            if (oppGoalY == -5000)
                            {
                                if (ballLocation.X < 700 && ballLocation.X > -700)
                                    complete = true;
                                else
                                {
                                    if (ballLocation.X < -700)
                                        controller.Steer = -largeSteering;
                                    else controller.Steer = largeSteering;
                                    complete = true;
                                }
                            }
                            else
                            {
                                controller.Steer = 1;
                                complete = true;
                            }
                        }
                        else if (ballTrajectoryX < -700 && ballLocation.X > -700)
                        {
                            controller.Steer = -smallSteering;
                            complete = true;
                        }

                        else if (ballTrajectoryX > 700 && ballLocation.X < 700)
                        {
                            controller.Steer = smallSteering;



                            complete = true;
                        }

                        if (botFrontToBallAngle > DegreesToRadians(180)) controller.Steer *= -1;

                        if (ballVelocity.X == 0 && ballVelocity.Y == 0) { complete = true; }
                        ballTrajectoryX += ballVelocity.X;
                        ballTrajectoryY += ballVelocity.Y;
                    }
                    //Console.WriteLine(controller.Steer);
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
                if ((DistanceToBall < DistanceToJump && !Jump1 && Math.Sqrt(botFrontToBallAngle * botFrontToBallAngle) < DegreesToRadians(10) && ballLocation.Z < 300 && !guidingBall) || (ballLocation.X == 0 && ballLocation.Y == 0 && DistanceToBall < DistanceToJump *2))
                {
                    controller.Jump = true;
                    Jump1 = true;
                    Jump1Time = DateTime.Now.Ticks;
                }

                if (botFrontToTargetAngle > 90 || botFrontToTargetAngle < -90)
                    controller.Throttle = 0.5f;
                else
                {
                    //speed calculation based on height of ball

                    //ball dropping
                    if (ballVelocity.Z < 0)
                    {
                        //work out landing point - or next contact with wall
                        var complete = false;

                        var ballTrajectoryX = ballLocation.X;
                        var ballTrajectoryY = ballLocation.Y;
                        var ballTrajectoryZ = ballLocation.Z;

                        while (!complete)
                        {

                            if ((Math.Abs(ballTrajectoryX) > 3800 || Math.Abs(ballTrajectoryY) > 5000) && !defenceMode)
                            {
                                controller.Throttle = 0;
                                controller.Boost = false;
                                complete = true;
                            }
                            else if (ballTrajectoryZ < 0)
                            {
                                var distance = Math.Abs(Get2DDistance(ballTrajectoryX, carLocation.X, ballTrajectoryY, carLocation.Y));
                                var distanceTravelled = Math.Abs(Get2DDistance(LastPositionX, carLocation.X, LastPositionY, carLocation.Y));
                                var ballVerticalDistance = LastBallPositionZ - ballLocation.Z;
                                if (ballVerticalDistance == 0)
                                {
                                    controller.Throttle = 1;
                                    complete = true;
                                }
                                else
                                {
                                    var framesToLand = Math.Ceiling(ballLocation.Z / ballVerticalDistance);
                                    var distanceInFrames = distanceTravelled * framesToLand;

                                    if (distanceInFrames < distance)
                                    {

                                        controller.Throttle = 1;
                                        controller.Boost = true;
                                        complete = true;

                                    }
                                    else
                                    {
                                        controller.Throttle = (float)(distance / distanceInFrames);
                                        complete = true;
                                    }

                                }


                                if (ballVelocity.X == 0 && ballVelocity.Y == 0)
                                {
                                    controller.Throttle = 1;
                                    controller.Boost = true;
                                    complete = true;
                                }
                            }
                                ballTrajectoryX += ballVelocity.X;
                                ballTrajectoryY += ballVelocity.Y;
                                ballTrajectoryZ += ballVelocity.Z;
                            

                        }
                    }
                    else
                    {
                        if (ballVelocity.Z == 0 || defenceMode) controller.Throttle = 1;
                        else
                        {
                            if ((team == 0 && carLocation.Y < ballLocation.Y) || (team == 1 && carLocation.Y > ballLocation.Y)) controller.Throttle = 1;
                            else {
                                if (DistanceToBall < 500) controller.Throttle = 0.05f;
                                else if (DistanceToBall < 1000) controller.Throttle = 0.1f;
                               else if (DistanceToBall < 2000) controller.Throttle = 0.5f;
                                else
                                controller.Throttle = 1;
                                Console.WriteLine(controller.Throttle);
                            }

                        }

                        if (Math.Abs(controller.Steer) == 1) controller.Throttle = 0.25f;
                    }
                   // controller.Throttle = 1 - (ballLocation.Z / 3000);
                }
                //if (DistanceToBall < 500 && ballLocation.Z > 500 && botToBallAngle <  DegreesToRadians(10))
                //{
                //    controller.Throttle = 0.1f;
                //}
                
                LastPositionX = carLocation.X;
                LastPositionY = carLocation.Y;
                LastBallPositionX = ballLocation.X;
                LastBallPositionY = ballLocation.Y;
                LastBallPositionZ = ballLocation.Z;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }


      
               
            //var buffer = new FlatBuffers.FlatBufferBuilder(1000);
            //QuickChat.StartQuickChat(buffer);
            //QuickChat.AddQuickChatSelection(buffer, QuickChatSelection.Compliments_WhatASave);

            //QuickChat.EndQuickChat(buffer);

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
