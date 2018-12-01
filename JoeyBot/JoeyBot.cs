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

        private enum Target
        {
            Undecided,
            Ball,
            BallLandingPoint,
            Goal,
            AwayFromGoal,
            BallInAir
        }

        private enum GameMode
        {
            Soccar,
            Hoops,
            Dropshot
        }

        private Target target;
        private Target prevTarget;
        private GameMode mode;
        private const float smallSteering = 0.1f;
        private const float medSteering = 0.2f;
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

                PlayerInfo playerInfo = gameTickPacket.Players(this.index).Value;
                if (playerInfo.Name == "BrokenJoey")
                {
                    controller.Throttle = 1;
                    controller.Steer = -0.3f;
                    controller.Boost = true;
                    return controller;
                }

                target = Target.Undecided;


                // Store the required data from the gameTickPacket.
                BallInfo ballInfo = gameTickPacket.Ball.Value;
                Vector3 ballLocation = ballInfo.Physics.Value.Location.Value;
                Vector3 carLocation = playerInfo.Physics.Value.Location.Value;
                
                Rotator carRotation = playerInfo.Physics.Value.Rotation.Value;
               Vector3 ballVelocity = ballInfo.Physics.Value.Velocity.Value;
                Vector3 carVelocity = playerInfo.Physics.Value.Velocity.Value;
                int ownGoalY = team == 0 ? -5000 : 5000;
                int oppGoalY = team == 0 ? 5000 : -5000;

                // Calculate to get the angle from the front of the bot's car to the target.
                //If car is in front of ball aim for goal, else aim for ball
                var ballTrajectoryX = ballLocation.X;
                var ballTrajectoryY = ballLocation.Y;
                var ballTrajectoryZ = ballLocation.Z;
                double botToTargetAngle;
                double botToBallAngle;
                double botToOppGoalAngle;
                var DistanceToBall = Get2DDistance(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);
                botToBallAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);




                float goalY = Math.Abs(GetFieldInfo().Goals(0).Value.Location.Value.Y);
                if (GetFieldInfo().GoalsLength > 2) mode = GameMode.Dropshot;
                else if (goalY == 5120) mode = GameMode.Soccar;
                else mode = GameMode.Hoops;

                int fieldLength = GetFieldLength(mode);

                //car is in goal
                if (GetFieldInfo().GoalsLength == 2 && (carLocation.Y < (goalY * -1) || carLocation.Y > goalY) )
                {
                    botToTargetAngle = Math.Atan2(-carLocation.Y, -carLocation.X);
                    target = Target.AwayFromGoal;
                }
                else if (ballLocation.X == 0 && ballLocation.Y == 0 && ballVelocity.Z == 0)
                {
                    target = Target.Ball;
                    botToTargetAngle = Math.Atan2(0 - carLocation.Y, 0 - carLocation.X);
                }
                else if (ballLocation.X == 0 && ballLocation.Y == 0 )
                {
                    target = Target.BallInAir;
                    botToTargetAngle = Math.Atan2(0 - carLocation.Y, 0 - carLocation.X);
                }

                //defending when blue
                else if (GetFieldInfo().GoalsLength == 2 && team == 0 && (carLocation.Y > ballLocation.Y || (prevTarget == Target.Goal && carLocation.Y > ballLocation.Y - 1000)))
                {
                    botToTargetAngle = Math.Atan2(goalY *-1 - carLocation.Y, 0 - carLocation.X);
                    controller.Boost = true;
                    target = Target.Goal;
                }
                //defending when orange
                else if (GetFieldInfo().GoalsLength == 2 && team == 1 && (carLocation.Y < ballLocation.Y || (prevTarget == Target.Goal && carLocation.Y < ballLocation.Y + 1000)))
                {
                    botToTargetAngle = Math.Atan2(goalY - carLocation.Y, 0 - carLocation.X);
                    controller.Boost = true;
                    target = Target.Goal;
                }
                else { botToTargetAngle = botToBallAngle;
                    if (ballVelocity.Z < 0 && carLocation.Z < 30)
                        target = Target.BallLandingPoint;
                    else {
                       if ( playerInfo.Boost < 30 || ballVelocity.Z == 0)
                        target = Target.Ball;
                        else
                        {
                            target = Target.BallInAir;
                        }
                        botToTargetAngle = botToBallAngle;
                    }
                }


                double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
                double botFrontToBallAngle = botToBallAngle - carRotation.Yaw;



                // Correct the angle
                botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);
                botFrontToBallAngle = CorrectAngle(botFrontToBallAngle);


                if (target == Target.BallInAir && Math.Abs(botFrontToBallAngle) < Math.Abs(DegreesToRadians(10)))
                {
                    if (carLocation.Z< 30)
                    {
                        controller.Jump = true;

                    }
                    controller.Boost = true;
                    controller.Pitch = SetPitchTarget(gameTickPacket, index, ballLocation.X, ballLocation.Y, ballLocation.Z);
                    //controller.Steer = SetSteerTarget(gameTickPacket, index, ballLocation.X, ballLocation.Y, ballLocation.Z);
                }
                else
                {

                    if (Math.Abs(carLocation.Y) <  4950 && Math.Abs(carLocation.X) > 1500 && (botFrontToTargetAngle > MinimumSteeringAngleRadians * 2 || botFrontToTargetAngle < MinimumSteeringAngleRadians * -2)) controller.Handbrake = true;
                    if (botFrontToTargetAngle > DegreesToRadians(135) || botFrontToTargetAngle < DegreesToRadians(-135)) controller.Handbrake = true;

                    if (carLocation.Z > 30) controller.Handbrake = false;


                    bool guidingBall = false;
                    // Decide which way to steer in order to get to the ball.

                    if (DistanceToBall <= 300 && botFrontToTargetAngle < MinimumSteeringAngleRadians / 2 && ballLocation.Z < 200 && ballLocation.X != 0 && ballLocation.Y != 0)
                    {
                        target = Target.Ball;
                        guidingBall = true;
                        var complete = false;

                        ballTrajectoryX = ballLocation.X;
                        ballTrajectoryY = ballLocation.Y;

                        while (!complete)
                        {
                            if (ballTrajectoryY > GetFieldLength(mode))
                            {
                                if (oppGoalY == 5000 && mode == GameMode.Soccar)
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
                            else if (ballTrajectoryY < GetFieldLength(mode) * -1)
                            {

                                if (oppGoalY == -5000 && mode == GameMode.Soccar)
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
                            else if (ballTrajectoryX < -700 && ballLocation.X > -700 && mode == GameMode.Soccar)
                            {
                                controller.Steer = -smallSteering;
                                complete = true;
                            }

                            else if (ballTrajectoryX > 700 && ballLocation.X < 700 && mode == GameMode.Soccar)
                            {
                                controller.Steer = smallSteering;



                                complete = true;
                            }

                            if (botFrontToBallAngle > DegreesToRadians(180)) controller.Steer *= -1;

                            if (ballVelocity.X == 0 && ballVelocity.Y == 0) { complete = true; }
                            if (!complete)
                            {
                                ballTrajectoryX += ballVelocity.X;
                                ballTrajectoryY += ballVelocity.Y;
                            }
                        }
                        //Console.WriteLine(controller.Steer);
                    }

                    else
                    {


                        if (target == Target.BallLandingPoint)
                        {
                            //speed calculation based on height of ball

                            //ball dropping
                            if (ballVelocity.Z < 0)
                            {
                                //work out landing point - or next contact with wall
                                var complete = false;



                                while (!complete)
                                {

                                    if (Math.Abs(ballTrajectoryX) > GetFieldWidth(mode) || Math.Abs(ballTrajectoryY) > GetFieldLength(mode))
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
                                    if (!complete)
                                    {
                                        ballTrajectoryX += ballVelocity.X;
                                        ballTrajectoryY += ballVelocity.Y;
                                        ballTrajectoryZ += ballVelocity.Z;
                                    }



                                }
                                controller.Steer = SetSteerTarget(gameTickPacket, index, ballTrajectoryX, ballTrajectoryY);


                            }
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





                        if (ballLocation.X == 0 && ballLocation.Y == 0 && ballLocation.Z < 300)
                        {
                            target = Target.Ball;
                            controller.Boost = true;
                            controller.Steer = SetSteerTarget(gameTickPacket, index, 0, 0);
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
                        if ((Jump1 && Jump2) || carLocation.Z <18 )
                        {
                            controller.Jump = false;
                            Jump1 = false;
                            Jump2 = false;
                            firstLoopComplete = false;
                            Jump1Time = 0;

                        }
                        else controller.Pitch = -1;

                        //First Jump
                        if ((DistanceToBall < DistanceToJump && !Jump1 && Math.Sqrt(botFrontToBallAngle * botFrontToBallAngle) < DegreesToRadians(10) && ballLocation.Z < 300 && !guidingBall) || (ballLocation.X == 0 && ballLocation.Y == 0 && DistanceToBall < DistanceToJump * 2))
                        {
                            controller.Jump = true;
                            Jump1 = true;
                            Jump1Time = DateTime.Now.Ticks;
                        }

                        if (botFrontToTargetAngle > 90 || botFrontToTargetAngle < -90)
                            controller.Throttle = 0.5f;

                        else if (target != Target.BallLandingPoint)
                        {
                            if (ballVelocity.Z == 0 || target == Target.Goal) controller.Throttle = 1;
                            else
                            {
                                if ((team == 0 && carLocation.Y < ballLocation.Y) || (team == 1 && carLocation.Y > ballLocation.Y)) controller.Throttle = 1;
                                else
                                {
                                    if (DistanceToBall < 500) controller.Throttle = 0.11f;
                                    else if (DistanceToBall < 1000) controller.Throttle = 0.1f;
                                    else if (DistanceToBall < 2000) controller.Throttle = 0.5f;
                                    else
                                        controller.Throttle = 1;

                                }

                            }

                            if (Math.Abs(controller.Steer) == 1) controller.Throttle = 0.25f;
                        }

                    }

                }
                switch (target)
                {
                    case Target.Ball:
                        Renderer.DrawLine3D(System.Windows.Media.Color.FromRgb(255, 0, 0), new System.Numerics.Vector3(carLocation.X, carLocation.Y, carLocation.Z), new System.Numerics.Vector3(ballLocation.X, ballLocation.Y, ballLocation.Z));
                        break;
                    case Target.BallInAir:
                        Renderer.DrawLine3D(System.Windows.Media.Color.FromRgb(255, 255, 0), new System.Numerics.Vector3(carLocation.X, carLocation.Y, carLocation.Z), new System.Numerics.Vector3(ballLocation.X, ballLocation.Y, ballLocation.Z));
                        break;
                    case Target.BallLandingPoint:
                        Renderer.DrawLine3D(System.Windows.Media.Color.FromRgb(0, 255, 0), new System.Numerics.Vector3(carLocation.X, carLocation.Y, carLocation.Z), new System.Numerics.Vector3(ballTrajectoryX, ballTrajectoryY, ballTrajectoryZ));

                        break;
                    case Target.Goal:
                        Renderer.DrawLine3D(System.Windows.Media.Color.FromRgb(0, 0, 255), new System.Numerics.Vector3(carLocation.X, carLocation.Y, carLocation.Z), new System.Numerics.Vector3(0, ownGoalY, 10));
                        break;
                    case Target.AwayFromGoal:
                        Renderer.DrawLine3D(System.Windows.Media.Color.FromRgb(0, 0, 0), new System.Numerics.Vector3(carLocation.X, carLocation.Y, carLocation.Z), new System.Numerics.Vector3(ballLocation.X, ballLocation.Y, ballLocation.Z));
                        break;
                    case Target.Undecided:
                        Renderer.DrawLine3D(System.Windows.Media.Color.FromRgb(255, 255, 255), new System.Numerics.Vector3(carLocation.X, carLocation.Y, carLocation.Z), new System.Numerics.Vector3(ballLocation.X, ballLocation.Y, ballLocation.Z));
                        break;
                }
                prevTarget = target;

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

        private int GetFieldLength(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Soccar:
                    return 5100;
                case GameMode.Hoops:
                    return 3250;
                case GameMode.Dropshot:
                    return 4200;
            }
            return 0;
        }

        private int GetFieldWidth(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Soccar:
                    return 3750;
                case GameMode.Hoops:
                    return 2700;
                case GameMode.Dropshot:
                    return 4600;
            }
            return 0;
        }

        private static float SetSteerTarget(GameTickPacket gameTickPacket, int index, double x, double y, double z = 0)
        {
            Vector3 carLocation = gameTickPacket.Players(index).Value.Physics.Value.Location.Value;
            Rotator carRotation = gameTickPacket.Players(index).Value.Physics.Value.Rotation.Value;
            var  botToTargetAngle = Math.Atan2(y - carLocation.Y, x - carLocation.X);
            double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
            botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);
            if (botFrontToTargetAngle == 0) return 0;
            if (botFrontToTargetAngle > 0 && botFrontToTargetAngle < DegreesToRadians(20)) return smallSteering;
            if (botFrontToTargetAngle > 0 && botFrontToTargetAngle < DegreesToRadians(100)) return medSteering;
            if (botFrontToTargetAngle > 0 && botFrontToTargetAngle > DegreesToRadians(100)) return largeSteering;
            if (botFrontToTargetAngle < 0 && Math.Abs(botFrontToTargetAngle) < DegreesToRadians(20)) return -smallSteering;
            if (botFrontToTargetAngle < 0 && botFrontToTargetAngle < DegreesToRadians(100)) return -medSteering;
            if (botFrontToTargetAngle < 0 && Math.Abs(botFrontToTargetAngle) > DegreesToRadians(100)) return -largeSteering;
            return 0;




        }

        private static float SetPitchTarget(GameTickPacket gameTickPacket, int index, double x, double y, double z = 0)
        {
            Vector3 carLocation = gameTickPacket.Players(index).Value.Physics.Value.Location.Value;
            Rotator carRotation = gameTickPacket.Players(index).Value.Physics.Value.Rotation.Value;

            var carToGroundAngle = Math.Atan2(0 - carLocation.Z, 0 - carRotation.Pitch);
            carToGroundAngle = CorrectAngle(carToGroundAngle);
            var botFrontToTargetAngle = carLocation.Z - z;
            //Console.WriteLine(carRotation.Pitch);
            if (botFrontToTargetAngle == 0) return 0;
            if (botFrontToTargetAngle > 0 && carRotation.Pitch > 0) return -0.1f;
            if (botFrontToTargetAngle < 0 && carRotation.Pitch < 0.5) return 0.5f;
          
            return 0;




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

        public static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180;
        }
    }
}
