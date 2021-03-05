using System;
using RLBotDotNet;
using rlbot.flat;
using KipjeBot.Actions;
using KipjeBot;
using System.Linq;
using KipjeBot.Utility;

namespace JoeyBot
{
    // We want to our bot to derive from Bot, and then implement its abstract methods.
    class JoeyBot : Bot
    {
        private string name;
        private int team;
        private int index;
        Random random = new Random();
        private DateTime lastChat;
        // We want the constructor for ExampleBot to extend from Bot, but we don't want to add anything to it.
        public JoeyBot(string botName, int botTeam, int botIndex) : base(botName, botTeam, botIndex)
        {

            name = botName;
            team = botTeam;
            index = botIndex;
            lastChat = DateTime.Now.AddSeconds(-10);
            prevTarget = Target.Undecided;
            
        }

        private enum Target
        {
            Undecided,
            Ball,
            BallLandingPoint,
            Goal,
            AwayFromGoal,
            BallInAir,
            Waiting,
            GuidingBall,
            Kickoff,
            SpecificPoint
        }

        private enum GameMode
        {
            Soccar,
            Hoops,
            Dropshot
        }

        private Target target;
        private Target prevTarget;
        Aerial aerial = null;
        private GameMode mode;
        private const float smallSteering = 0.3f;
        private const float medSteering = 0.7f;
        private const float largeSteering = 1f;
        private const float ownGoalTurnDistance = 850;
        private const int ballLandingPointHeight = 200;
        private const int maxAerialTrajectoryAngleDegrees = 10;
        private const int GoalieBallTargetY = 700;
        private const int GoalieWaitY = 2500;
        private const int MinTeammatesForWait = 2;
        static int MinimumSteeringAngleDegrees = 10;
        static double MinimumSteeringAngleRadians = DegreesToRadians(MinimumSteeringAngleDegrees);
        static int DistanceToJump = 325;
        private System.Collections.Generic.List<Car> teammates;
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
        private double TargetPointX;
        private double TargetPointY;

        private KipjeBot.GameInfo gameInfo;
        private KipjeBot.GameTickPacket.FieldInfo fieldInfo;
        private BallPredictionCollection ballPrediction;
        private int MinTeammatesForGK = 2;

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
                if (gameInfo == null)
                {
                gameInfo = new KipjeBot.GameInfo(index, team, name);
                    SetTeammates();
                }
                if(fieldInfo == null)
                fieldInfo = new KipjeBot.GameTickPacket.FieldInfo(GetFieldInfo());
                if (ballPrediction == null)
                ballPrediction = new BallPredictionCollection();


                gameInfo.Update(gameTickPacket, GetRigidBodyTick());
                ballPrediction.Update(GetBallPrediction());

                Car car = gameInfo.Cars[index];
                Ball ball = gameInfo.Ball;

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
                Vector3 carVelocityStruct = playerInfo.Physics.Value.Velocity.Value;

                System.Numerics.Vector3 carVelocity = new System.Numerics.Vector3(carVelocityStruct.X, carVelocityStruct.Y, carVelocityStruct.Z);
                var velocityValue = Math.Sqrt(carVelocity.LengthSquared());
                int ownGoalY = team == 0 ? -5000 : 5000;
                int oppGoalY = team == 0 ? 5000 : -5000;

                // Calculate to get the angle from the front of the bot's car to the target.
                //If car is in front of ball aim for goal, else aim for ball
                var ballTrajectoryX = ballLocation.X;
                var ballTrajectoryY = ballLocation.Y;
                var ballTrajectoryZ = ballLocation.Z;
                double botToTargetAngle = 0;
                double botToBallAngle;
                double botToOppGoalAngle;
                var DistanceToBall = Get2DDistance(carLocation.X, ballLocation.X, carLocation.Y, ballLocation.Y);
                botToBallAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
                double botFrontToBallAngle = botToBallAngle - carRotation.Yaw;
                botFrontToBallAngle = CorrectAngle(botFrontToBallAngle);
                float goalY = Math.Abs(GetFieldInfo().Goals(0).Value.Location.Value.Y);
                if (GetFieldInfo().GoalsLength > 2) mode = GameMode.Dropshot;
                else if (goalY == 5120) mode = GameMode.Soccar;
                else mode = GameMode.Hoops;

                botToOppGoalAngle = Math.Atan2(oppGoalY - carLocation.Y, 0 - carLocation.X);
                double botFrontToOppGoalAngle = botToOppGoalAngle - carRotation.Yaw;
                botFrontToOppGoalAngle = CorrectAngle(botFrontToOppGoalAngle);
                int fieldLength = GetFieldLength(mode);
                controller.Jump = false;
                controller.Handbrake = false;

                //work out state code

                if (teammates == null)
                {
                    SetTeammates();
                }


                if (prevTarget == Target.Waiting && gameInfo.IsKickOffPause)
                {
                    target = Target.Waiting;

                }
                else if (gameInfo.IsKickOffPause && gameInfo.IsRoundActive)
                {
                    target = Target.Kickoff;
                }


                //goalkeeper mode
                else if (teammates.Count() >= MinTeammatesForGK  && ((ownGoalY < 0 && carLocation.Y < (from x in teammates select x.Position.Y).Min()) || (ownGoalY > 0 && carLocation.Y > (from x in teammates select x.Position.Y).Max())))
                {
                    //if (target != Target.AwayFromGoal)
                    //{

                    //team 0 = blue team, defending negative, attacking positive
                    if (team == 0)
                    {

                        if (ballLocation.Y < GoalieBallTargetY * -1)
                        {
                            target = Target.Ball;
                            botToTargetAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
                        }
                        else if (ballLocation.Y > 1000)
                        {

                            if (Math.Abs(ballLocation.X) < 800)
                            {
                                target = Target.Ball;
                                botToTargetAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
                            }
                            else if (carLocation.Y < -1000 || Math.Abs(botFrontToBallAngle) > DegreesToRadians(30))
                            {
                                target = Target.SpecificPoint;
                                TargetPointX = 0;
                                TargetPointY = 0;
                            }
                            else { target = Target.Waiting; }
                        }
                        else if (carLocation.Y < GoalieWaitY * -1 && Math.Abs(carLocation.X) < 700 && Math.Abs(botFrontToBallAngle) < DegreesToRadians(45))
                        {
                            target = Target.Waiting;
                        }
                        else
                        {
                            target = Target.Goal;
                            botToTargetAngle = Math.Atan2(goalY * -1 - carLocation.Y, 0 - carLocation.X);
                        }
                    }
                    else
                    //team 1 = orange team, defending positive, attacking negative

                    {
                        if (ballLocation.Y > GoalieBallTargetY)
                        {
                            target = Target.Ball;
                            botToTargetAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);

                        }
                        else if (ballLocation.Y < -1000)
                        {
                            if (Math.Abs(ballLocation.X) < 800)
                            {
                                target = Target.Ball;
                                botToTargetAngle = Math.Atan2(ballLocation.Y - carLocation.Y, ballLocation.X - carLocation.X);
                            }

                            else if (carLocation.Y > 1000 || Math.Abs(botFrontToBallAngle) > DegreesToRadians(30))
                            {
                                target = Target.SpecificPoint;
                                TargetPointX = 0;
                                TargetPointY = 0;
                            }
                            else { target = Target.Waiting; }
                        }
                        else if (carLocation.Y > GoalieWaitY && Math.Abs(carLocation.X) < 700 && Math.Abs(botFrontToBallAngle) < DegreesToRadians(45))
                        {
                            target = Target.Waiting;
                        }
                        else
                        {
                            target = Target.Goal;
                            botToTargetAngle = Math.Atan2(goalY - carLocation.Y, 0 - carLocation.X);
                        }

                    }
                    //}
                }



                //car is in goal
                else if (GetFieldInfo().GoalsLength == 2 && ((Math.Abs(carLocation.Y) > Math.Abs(goalY) - ownGoalTurnDistance && (prevTarget == Target.Goal || prevTarget == Target.AwayFromGoal)) || carLocation.Y > Math.Abs(goalY)))
                {
                    botToTargetAngle = Math.Atan2(-carLocation.Y, 0);
                    if (Math.Abs(ballLocation.X) < 1000) target = Target.Ball;
                    else
                    {
                        target = Target.AwayFromGoal;
                    }
                }

                else if (ballLocation.X == 0 && ballLocation.Y == 0)
                {
                    target = Target.Ball;
                    botToTargetAngle = Math.Atan2(0 - carLocation.Y, 0 - carLocation.X);
                }

                //defending when blue
                else if (GetFieldInfo().GoalsLength == 2 && team == 0 && (carLocation.Y > ballLocation.Y || (prevTarget == Target.Goal && carLocation.Y > ballLocation.Y - 1000))
                    && ballVelocity.Y < 0)
                {
                    botToTargetAngle = Math.Atan2(goalY * -1 - carLocation.Y, 0 - carLocation.X);
                    controller.Boost = true;
                    target = Target.Goal;
                }
                //defending when orange
                else if (GetFieldInfo().GoalsLength == 2 && team == 1 && (carLocation.Y < ballLocation.Y || (prevTarget == Target.Goal && carLocation.Y < ballLocation.Y + 1000)) && ballVelocity.Y > 0)
                {
                    botToTargetAngle = Math.Atan2(goalY - carLocation.Y, 0 - carLocation.X);
                    controller.Boost = true;
                    target = Target.Goal;
                }






                else
                {

                    var goForBall = true;

                    if (teammates.Count >= MinTeammatesForWait)
                    {

                        var distanceToBall = car.DistanceToBall(ball);
                        var teammateDistance = (from x in teammates select x.DistanceToBall(ball)).Min();

                        if (teammateDistance < distanceToBall && teammateDistance < 2000) goForBall = false;


                    if (Math.Abs(ballLocation.X) < 1000 && teammateDistance > 500 && teammateDistance < 1500)
                    {
                        goForBall = true;
                    }

                    }

                    if (goForBall)
                    {

                        botToTargetAngle = botToBallAngle;
                        var trajectoryAngle = Math.Atan2(ballVelocity.Y - carVelocity.Y, ballVelocity.X - carVelocity.X);

                        if (playerInfo.Boost > 10 && ballLocation.Z > 700 && Math.Abs(botFrontToBallAngle) < Math.Abs(DegreesToRadians(20)) && (Math.Abs(trajectoryAngle) < DegreesToRadians(maxAerialTrajectoryAngleDegrees) || Math.Abs(trajectoryAngle) > 180 - maxAerialTrajectoryAngleDegrees))
                            target = Target.BallInAir;
                        else
                        {
                            if (ballLocation.Z > ballLandingPointHeight)
                            {
                                target = Target.BallLandingPoint;
                            }
                            else
                            {

                                target = Target.Ball;
                            }
                        }
                        botToTargetAngle = botToBallAngle;
                    }
                    else
                    {
                       if (Math.Abs(carLocation.X) < 800 && Math.Abs(botFrontToOppGoalAngle)  < DegreesToRadians(30))
                        {
                            //if ball behind, reposition other side of ball

                            target = Target.Waiting;
                            //blue
                            if (team == 0)
                            {
                                if (carLocation.Y > ballLocation.Y)
                                {
                                    target = Target.SpecificPoint;
                                    TargetPointX = carLocation.X;
                                    TargetPointY = ballLocation.Y - 1000;

                                }
                            
                            }


                            //orange
                            else
                            { 
                                if (carLocation.Y < ballLocation.Y)
                                {
                                    target = Target.SpecificPoint;
                                    TargetPointX = carLocation.X;
                                    TargetPointY = ballLocation.Y + 1000;

                                }
                            }

                        }
                        else
                        {


                            TargetPointX = 0;
                            TargetPointY = team == 0 ? 2000 : -2000 ;
                            if (team == 0 && ballLocation.Y < 2000) TargetPointY = ballLocation.Y - 1000;
                            if (team == 1 && ballLocation.Y > -2000) TargetPointY = ballLocation.Y + 1000;
                            target = Target.SpecificPoint;
                        }


                    }
                }
                    
                


                double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;



                // Correct the angle
                botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);
               


                if (DistanceToBall <= 300 && botFrontToTargetAngle < MinimumSteeringAngleRadians / 2 && ballLocation.Z < 200 && ballLocation.X != 0 && ballLocation.Y != 0)
                {
                    target = Target.GuidingBall;
                }


                if (target == Target.Ball)
                {
                    Slice[] slices = ballPrediction.ToArray();
                    float targetTime = 0;
                    double ballVelocityUnit = Math.Sqrt(ballVelocity.X * ballVelocity.X + ballVelocity.Y * ballVelocity.Y);
                    if (ballVelocityUnit < 200 || DistanceToBall < 600) targetTime = 0.1f;
                    else if (ballVelocityUnit < 400) targetTime = 0.6f;
                    else if (ballVelocityUnit < 600) targetTime = 1.25f;
                    else targetTime = 2;
                    Slice? targetSlice = slices.Where(x => x.Time - gameInfo.Time >= targetTime).FirstOrDefault();
                    if (targetSlice != null)
                    {
                        target = Target.SpecificPoint;
                        TargetPointX = targetSlice.Value.Position.X;
                        TargetPointY = targetSlice.Value.Position.Y;
                    }
                }


                switch (target)
                {
                    case Target.BallInAir:
                        Slice[] slices = ballPrediction.ToArray();
                        

                        if (aerial == null)
                        {
                            for (int i = 0; i < slices.Length; i++)
                            {
                                float B_avg = Aerial.CalculateCourse(car, slices[i].Position, slices[i].Time - gameInfo.Time).Length();
                                if (B_avg < 970)
                                {

                                    aerial = new Aerial(car, slices[i].Position, gameInfo.Time, slices[i].Time);
                                    break;
                                }

                            }
                            controller.Jump = true;
                        }
                        else
                        {
                            controller = aerial.Step(ball, 0.0083335f, gameInfo.Time);
                            controller.Boost = true;

                            if (aerial.Finished)
                            {
                                aerial = null;
                            }
                            else
                            {
                                for (int i = 0; i < slices.Length; i++)
                                {
                                    if (Math.Abs(slices[i].Time - aerial.ArrivalTime) < 0.02)
                                    {
                                        if ((aerial.Target - slices[i].Position).Length() > 40)
                                            aerial = null;
                                        break;
                                    }
                                }
                            }
                        }
                        break;

                    case Target.BallLandingPoint:
                        if (ballVelocity.X == 0 && ballVelocity.Y == 0)
                        {
                            controller.Throttle = 1;
                            controller.Boost = true;

                        }
                        else
                        {


                            //speed calculation based on height of ball

                            var slice = ballPrediction.GetNextGroundTouch();


                            if (slice != null)
                            {

                                var target = slice.Value;



                                var distance = Math.Abs(Get2DDistance(target.Position.X, carLocation.X, target.Position.Y, carLocation.Y)) + 100;


                                var time = target.Time - gameTickPacket.GameInfo.Value.SecondsElapsed;
                                var ticks = time * 120;
                                var currentDistance = ticks * velocityValue;



                                if (currentDistance < distance)
                                {

                                    controller.Throttle = 1;
                                    controller.Boost = true;


                                }
                                else
                                {
                                    controller.Throttle = 0.1f;
                                    controller.Boost = false;


                                }



                            }


                            controller = SetSteeringBasedOnTarget(controller, botFrontToTargetAngle);
                        }
                        break;

                    case Target.GuidingBall:
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
                        controller.Throttle = 1;
                        break;
                    case Target.Kickoff:

                        if (teammates == null)
                        {
                            SetTeammates();
                        }
                        if (teammates.Count() == 0)
                        {
                            controller = GoForKickoff(gameTickPacket, controller);

                            SetJump(ref controller, ref ballLocation, ref carLocation, DistanceToBall, botFrontToBallAngle);
                        }
                        else
                        {
                            var furthestTeammate = (from x in teammates select x.DistanceToCentre()).Max();
                            if (gameInfo.MyCar.DistanceToCentre() <= furthestTeammate)
                            {
                               controller = GoForKickoff(gameTickPacket, controller);

                                SetJump(ref controller, ref ballLocation, ref carLocation, DistanceToBall, botFrontToBallAngle);
                            }
                            else
                            {
                                target = Target.Waiting;

                                controller.Throttle = 0;
                                controller.Handbrake = true;
                                controller.Steer = 0;
                            }
                        }
                        break;

                    case Target.SpecificPoint:
                        botToTargetAngle = Math.Atan2(TargetPointY - carLocation.Y, TargetPointX - carLocation.X);
                         botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
                        

                        // Correct the angle
                        botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);

                        SetHandbrakeAndBoost(ref controller, ref carLocation, carVelocity, botFrontToTargetAngle);

                        SetJump(ref controller, ref ballLocation, ref carLocation, DistanceToBall, botFrontToBallAngle);


                        SetThrottle(ref controller, ref ballLocation, ref carLocation, ballVelocity, DistanceToBall, botFrontToTargetAngle);

                        controller = SetSteeringBasedOnTarget(controller, botFrontToTargetAngle);

                        break;



                      case Target.Waiting:
                       
                        controller.Throttle = 0;
                  
                        break;

                    default:
                        SetHandbrakeAndBoost(ref controller, ref carLocation, carVelocity, botFrontToTargetAngle);

                        SetJump(ref controller, ref ballLocation, ref carLocation, DistanceToBall, botFrontToBallAngle);


                        SetThrottle(ref controller, ref ballLocation, ref carLocation, ballVelocity, DistanceToBall, botFrontToTargetAngle);

                      controller =  SetSteeringBasedOnTarget(controller, botFrontToTargetAngle);
                        if (target == Target.AwayFromGoal && Math.Abs(botFrontToTargetAngle) > DegreesToRadians(90) )
                            controller.Handbrake = true;
                        if (target == Target.AwayFromGoal) controller.Boost = false;

                        break;
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
                if (DateTime.Now > lastChat.AddSeconds(10) && random.NextDouble() < 0.00033)
                {

                    int chatNo = (int)(random.NextDouble() * 63);
                    try
                    {
                        SendQuickChatFromAgent(false, (QuickChatSelection)chatNo);
                    }
                    catch
                    {
                        Console.WriteLine("QuickChat Exception: " + chatNo);
                    }
                    lastChat = DateTime.Now;
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }


          

            return controller;
        }

        private void SetTeammates()
        {
            teammates = gameInfo.Cars.Where(x => x.Team == this.team && x.Name != this.name).ToList();
        }

        private void SetThrottle(ref Controller controller, ref Vector3 ballLocation, ref Vector3 carLocation, Vector3 ballVelocity, double DistanceToBall, double botFrontToTargetAngle)
        {
     

            if (target != Target.BallLandingPoint && target != Target.Waiting)
            {
                if (ballVelocity.Z == 0 || target == Target.Goal) controller.Throttle = 1;
                else
                {
                    if ((team == 0 && carLocation.Y < ballLocation.Y) || (team == 1 && carLocation.Y > ballLocation.Y)) controller.Throttle = 1;
                    else
                    {
                        //if (DistanceToBall < 500) controller.Throttle = 0.4f;

                        //else if (DistanceToBall < 1000) controller.Throttle = 0.6f;
                        //else if (DistanceToBall < 2000) controller.Throttle = 0.8f;
                        //else
                            controller.Throttle = 1;

                    }

                }


                if (Math.Abs(controller.Steer) == 1) controller.Throttle = 0.25f;
            }
        }

        private void SetJump(ref Controller controller, ref Vector3 ballLocation, ref Vector3 carLocation, double DistanceToBall, double botFrontToBallAngle)
        {
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
            else if (target != Target.Waiting) controller.Throttle = 1;

            //After second jump
            if ((Jump1 && Jump2) || carLocation.Z < 18)
            {
                controller.Jump = false;
                Jump1 = false;
                Jump2 = false;
                firstLoopComplete = false;
                Jump1Time = 0;

            }
            else controller.Pitch = -1;

            //First Jump
            if ((DistanceToBall < DistanceToJump && !Jump1 && Math.Sqrt(botFrontToBallAngle * botFrontToBallAngle) < DegreesToRadians(10) && ballLocation.Z < 300) || (ballLocation.X == 0 && ballLocation.Y == 0 && DistanceToBall < DistanceToJump * 2))
            {
                controller.Jump = true;
                Jump1 = true;
                Jump1Time = DateTime.Now.Ticks;
            }
        }

        private static Controller SetSteeringBasedOnTarget(Controller controller, double botFrontToTargetAngle)
        {
            if (botFrontToTargetAngle > 0)
                if (botFrontToTargetAngle > MinimumSteeringAngleRadians)
                    if (botFrontToTargetAngle > DegreesToRadians(90))
                        controller.Steer = largeSteering;
                    else if (botFrontToTargetAngle < DegreesToRadians(45))
                    {
                        controller.Steer = smallSteering;
                    }
                    else controller.Steer = medSteering;
                else controller.Steer = 0;
            else
                                                    if (botFrontToTargetAngle < MinimumSteeringAngleRadians * -1)
                if (botFrontToTargetAngle < DegreesToRadians(-90))
                    controller.Steer = largeSteering * -1 ;
                else if (botFrontToTargetAngle > DegreesToRadians(-45))
                {
                    controller.Steer = smallSteering * -1;
                }
                else controller.Steer = medSteering * -1;
            else controller.Steer = 0;
            return controller;
        }

        private static void SetHandbrakeAndBoost(ref Controller controller, ref Vector3 carLocation, System.Numerics.Vector3 carVelocity, double botFrontToTargetAngle)
        {
            if (Math.Abs(botFrontToTargetAngle) > DegreesToRadians(100) && Math.Sqrt(carVelocity.LengthSquared()) > 0.6)
            {
                controller.Handbrake = true;
                controller.Boost = false;
            }


            if (carLocation.Z > 50) controller.Handbrake = false;

            if (Math.Abs(botFrontToTargetAngle) < MinimumSteeringAngleRadians) controller.Boost = true;

      

        }

        private Controller GoForKickoff(GameTickPacket gameTickPacket, Controller controller)
        {
            
            target = Target.Ball;
            controller.Boost = true;
            controller.Steer = SetSteerTarget(gameTickPacket, index, 0, 0);
            if (controller.Steer > 0.2) controller.Steer = 0.2f;
            if (controller.Steer < -0.2) controller.Steer = -0.2f;

            controller.Throttle = 1;
    
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
            var botToTargetAngle = Math.Atan2(y - carLocation.Y, x - carLocation.X);
            double botFrontToTargetAngle = botToTargetAngle - carRotation.Yaw;
            botFrontToTargetAngle = CorrectAngle(botFrontToTargetAngle);
       
            if (botFrontToTargetAngle == 0) return 0;
            if (botFrontToTargetAngle > 0 && botFrontToTargetAngle < DegreesToRadians(45)) return smallSteering;
            if (botFrontToTargetAngle > 0 && botFrontToTargetAngle < DegreesToRadians(90)) return medSteering;
            if (botFrontToTargetAngle > 0 && botFrontToTargetAngle > DegreesToRadians(90)) return largeSteering;
            if (botFrontToTargetAngle < 0 && Math.Abs(botFrontToTargetAngle) < DegreesToRadians(45)) return -smallSteering;
            if (botFrontToTargetAngle < 0 && Math.Abs( botFrontToTargetAngle) < DegreesToRadians(90)) return -medSteering;
            if (botFrontToTargetAngle < 0 && Math.Abs(botFrontToTargetAngle) > DegreesToRadians(90)) return -largeSteering;
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
