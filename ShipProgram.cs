using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        double errorDistance = 300; //if farther than this distance from docking connector while scooching back, then stop.
        double finalOutDistance = 100; //how far out to place final waypoint from original 'first' point when departing
        
        Boolean customPasswordMode = false;
        string customPassword = "The rawr";

        double lengthOfShip;

        IMyRadioAntenna antenna;
        IMyRemoteControl rc;
        IMyShipConnector connector;

        IMyGyro gyro;
        List<IMyGyro> gyros = new List<IMyGyro>();

        List<IMyThrust> allThrusters;
        List<IMyThrust> backThrusterGroup = new List<IMyThrust>();
        List<IMyThrust> leftThrusterGroup = new List<IMyThrust>();
        List<IMyThrust> rightThrusterGroup = new List<IMyThrust>();
        List<IMyThrust> upThrusterGroup = new List<IMyThrust>();
        List<IMyThrust> downThrusterGroup = new List<IMyThrust>();

        List<List<IMyThrust>> allThrustGroups = new List<List<IMyThrust>>();

        float totalBackForce = 0;

        Vector3D dockingConnectorPos;
        Vector3D dockingDir;

        string groupName = "default";

        Base6Directions.Direction rcDockingDir;

        float shipMass;

        Vector3D antennaPos = new Vector3D();
        List<string> messageQue = new List<string>();

        MyIni ini = new MyIni();

        List<MyWaypointInfo> globalPath = new List<MyWaypointInfo>();
        List<MyWaypointInfo> reversePath = new List<MyWaypointInfo>();

        Boolean collisionAvoidance = true;

        Boolean goodsetup = true;

        string password;
        Boolean waiting = false;
        string linkedConnectorName;

        int mode = 0; //0 idle, 1 going through path, 2 going to docking coord, 3 docking, 4 going back

        Boolean lastReversePath = true;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;
            ini.TryParse(Storage);

            antenna = GridTerminalSystem.GetBlockWithName("Antenna Miner#1") as IMyRadioAntenna;
            rc = GridTerminalSystem.GetBlockWithName("rc") as IMyRemoteControl;
            gyro = GridTerminalSystem.GetBlockWithName("Gyroscope") as IMyGyro;
            connector = GridTerminalSystem.GetBlockWithName("Connector") as IMyShipConnector;
            connector.PullStrength = float.PositiveInfinity;

            if (connector.Status == MyShipConnectorStatus.Connected)
                goodsetup = false;

            shipMass = rc.CalculateShipMass().TotalMass;

            if (connector.Orientation.Forward == rc.Orientation.Forward)
                rcDockingDir = Base6Directions.Direction.Forward;
            else if (connector.Orientation.Forward == Base6Directions.GetFlippedDirection(rc.Orientation.Forward))
                rcDockingDir = Base6Directions.Direction.Backward;
            else if (connector.Orientation.Forward == rc.Orientation.Left)
                rcDockingDir = Base6Directions.Direction.Left;
            else if (connector.Orientation.Forward == Base6Directions.GetFlippedDirection(rc.Orientation.Left))
                rcDockingDir = Base6Directions.Direction.Right;
            else if (connector.Orientation.Forward == rc.Orientation.Up)
                rcDockingDir = Base6Directions.Direction.Up;
            else if (connector.Orientation.Forward == Base6Directions.GetFlippedDirection(rc.Orientation.Up))
                rcDockingDir = Base6Directions.Direction.Down;
            else
                Echo("ERROR: ORIENTATION MATCHING FAILED");

            BoundingBoxD boundingBox = rc.CubeGrid.WorldAABB;
            lengthOfShip = Vector3D.Distance(boundingBox.Max, boundingBox.Min);


            gyros = new List<IMyGyro>();
            GridTerminalSystem.GetBlocksOfType(gyros);

            foreach(IMyGyro gyro in gyros) {
                gyro.Pitch = 0;
                gyro.Yaw = 0;
                gyro.GyroOverride = false;
                gyro.ApplyAction("OnOff_On");
            }

            if (ini.ContainsKey("save","password"))
                password = ini.Get("save", "password").ToString();
            else if (customPasswordMode)
                password = customPassword;
            else
            {
                Random r = new Random();
                password = r.Next(1000000, 9999999) + "";
            }

            allThrusters = new List<IMyThrust>();
            GridTerminalSystem.GetBlocksOfType(allThrusters);

            allThrustGroups.Add(leftThrusterGroup);
            allThrustGroups.Add(rightThrusterGroup);
            allThrustGroups.Add(upThrusterGroup);
            allThrustGroups.Add(downThrusterGroup);
            allThrustGroups.Add(backThrusterGroup);

            foreach (IMyThrust thruster in allThrusters)
            {
                if (thruster.Orientation.Forward == Base6Directions.GetFlippedDirection(connector.Orientation.Up))
                    downThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == connector.Orientation.Up)
                    upThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == connector.Orientation.Left)
                    leftThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == Base6Directions.GetFlippedDirection(connector.Orientation.Left))
                    rightThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == Base6Directions.GetFlippedDirection(connector.Orientation.Forward)) {
                    backThrusterGroup.Add(thruster);
                    totalBackForce += thruster.MaxEffectiveThrust;
                }
            }

            if (ini.ContainsKey("save", "linkedConnectorName"))
                linkedConnectorName = ini.Get("save", "linkedConnectorName").ToString();
            if (ini.ContainsKey("save", "groupName"))
                linkedConnectorName = ini.Get("save", "groupName").ToString();
            if (ini.ContainsKey("save", "dockingConnectorPos"))
                Vector3D.TryParse(ini.Get("save", "dockingConnectorPos").ToString(), out dockingConnectorPos);
            if (ini.ContainsKey("save", "dockingDir"))
                Vector3D.TryParse(ini.Get("save", "dockingDir").ToString(), out dockingDir);
            
            if (ini.ContainsKey("save", "mode"))
                Int32.TryParse(ini.Get("save", "mode").ToString(), out mode);
            var k = 0;
            MyWaypointInfo element;
            globalPath.Clear();
            while (ini.ContainsKey("save", "globalPath_" + k))
            {
                MyWaypointInfo.TryParse(ini.Get("save", "globalPath_" + k).ToString(), out element);
                globalPath.Add(element);
                k++;
            }

            reversePath.Clear();
            reversePath = globalPath;
            reversePath.Reverse();
        }

        public void Save()
        {
            ini.Clear();

            ini.Set("save", "password", password);
            ini.Set("save", "linkedConnectorName", linkedConnectorName);
            ini.Set("save", "groupName", groupName);

            ini.Set("save", "dockingConnectorPos", dockingConnectorPos.ToString());
            ini.Set("save", "dockingDir", dockingDir.ToString());

            for (var i = 0; i < globalPath.Count; i++)
                ini.Set("save", "globalPath_" + i, globalPath[i].ToString());

            ini.Set("save", "mode", mode);

            Storage = ini.ToString();

        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (goodsetup)
            {
                Echo("LastR: " + lastReversePath);
                Echo("Mode: " + mode);
                Echo("DockingDir: " + rcDockingDir.ToString());
                Echo("Password: " + password);

                CheckMessageQue();

                if (!String.IsNullOrEmpty(argument))
                {
                    Echo("Argument: " + argument);
                    string[] info = argument.Split(new string[] { "," }, StringSplitOptions.None);

                    if (updateSource != UpdateType.Antenna)
                    {
                        if (info[0].ToLower() == "dock")
                        {
                            if (info.Length == 1)
                            {
                                Boolean sent = antenna.TransmitMessage("requestdock," + password + ",default");
                                if (!sent)
                                    messageQue.Add("requestdock," + password + ",default");
                            }
                            else if (info.Length == 2)
                            {
                                Boolean sent = antenna.TransmitMessage("requestdock," + password + "," + info[1]);
                                if (!sent)
                                    messageQue.Add("requestdock," + password + "," + info[1]);
                                groupName = info[1];
                            }
                            else if (info.Length == 3)
                            {
                                Boolean sent = antenna.TransmitMessage("requestdock," + password + "," + groupName + "," + info[2]);
                                if (!sent)
                                    messageQue.Add("requestdock," + password + "," + groupName + "," + info[2]);
                            }
                            else
                                Echo("ERROR, ARGUMENT SIZE INVALID");
                        }
                        else if (info[0].ToLower() == "stop")
                        {
                            Storage = "";
                            dockingConnectorPos = new Vector3D();
                            dockingDir = new Vector3D();
                            mode = 0;
                            globalPath.Clear();
                            reversePath.Clear();
                            rc.ClearWaypoints();

                            foreach (IMyThrust thrust in allThrusters)
                                thrust.ThrustOverridePercentage = 0;

                            foreach (IMyGyro gyro in gyros)
                            {
                                gyro.GyroOverride = false;
                                gyro.Pitch = 0;
                                gyro.Yaw = 0;
                                gyro.Roll = 0;
                            }

                            Boolean sent = antenna.TransmitMessage("canceldock," + password);
                            if (!sent) messageQue.Add("canceldock," + password);
                        }

                        else if (info[0] == "depart")
                        {
                            if (mode == 5 && connector.Status == MyShipConnectorStatus.Connected)
                                Main("dock," + groupName + ",leaving", UpdateType.Mod);
                            else
                                Echo("ERROR, WRONG MODE OR ALREADY DISCONNECTED");
                        }

                        else if (info[0].ToLower() == "newpassword")
                        {
                            Random r = new Random();
                            password = r.Next(1000000, 9999999) + "";
                        }
                    }

                    else if (updateSource == UpdateType.Antenna && info[0] == password)
                    {
                        Echo("Message Received: " + argument);
                        if (info[1] == "received" && info.Length == 3) //info[2] is name of spaceport
                            Echo("Request to '" + info[2] + "' was received, awaiting further instruction.");
                        else if (info[1] == "fail")
                            Echo("Request to '" + info[3] + "' failed.");
                        else if (info[1] == "rejected")
                        {
                            Echo("TOOK TO LONG, DOCKING PERMISSION REJECTED");
                            mode = 0;
                            rc.SetAutoPilotEnabled(false);
                            antenna.CustomName += " ERROR";
                        }
                        else if (info[1] == "wait")
                        {
                            Echo("Request to '" + info[3] + "' success, but placed into waiting que");
                            waiting = true;
                        }
                        else if (info[1] == "dockinginfo")
                        {
                            if (mode == 5)
                            {
                                connector.Disconnect();
                                List<MyWaypointInfo> path = new List<MyWaypointInfo>();
                                string[] strWaypoints = new string[info.Length - 5];

                                for (int i = 0; i < strWaypoints.Length; i++)
                                {
                                    strWaypoints[i] = info[i + 5];
                                }

                                foreach (string waypoint in strWaypoints)
                                {
                                    MyWaypointInfo newPoint;
                                    Boolean pass = MyWaypointInfo.TryParse(waypoint, out newPoint);
                                    if (pass)
                                        path.Add(newPoint);
                                    else
                                        break;
                                }

                                path.Reverse();
                                reversePath = path;
                                EnableRC(reversePath, out path);
                                mode = 6;
                            }
                            else if (info.Length == 5)
                            {
                                linkedConnectorName = info[2];
                                string strConnectorPos = info[3];
                                string strDockingDir = info[4];

                                //parse str's into their proper values
                                Boolean pass1 = Vector3D.TryParse(strConnectorPos, out dockingConnectorPos);
                                Boolean pass2 = Vector3D.TryParse(strDockingDir, out dockingDir);

                                Dock2(dockingConnectorPos, dockingDir); mode = 2;
                            }
                            else if (info.Length > 5)
                            {
                                linkedConnectorName = info[2];
                                string strConnectorPos = info[3];
                                string strDockingDir = info[4];
                                string[] strWaypoints = new string[info.Length - 5];
                                for (int i = 0; i < strWaypoints.Length; i++)
                                    strWaypoints[i] = info[i + 5];

                                //parse str's into their proper values
                                Boolean pass1 = Vector3D.TryParse(strConnectorPos, out dockingConnectorPos);
                                Boolean pass2 = Vector3D.TryParse(strDockingDir, out dockingDir);
                                pass2 = false;

                                List<MyWaypointInfo> path = new List<MyWaypointInfo>();
                                Boolean pass3 = true;
                                foreach (string waypoint in strWaypoints)
                                {
                                    pass2 = true;
                                    MyWaypointInfo newPoint;
                                    pass3 = MyWaypointInfo.TryParse(waypoint, out newPoint);
                                    if (pass3) path.Add(newPoint);
                                    else break;
                                }

                                if (pass1 && pass2 && pass3)
                                {
                                    EnableRC(path, out globalPath);
                                    reversePath.Reverse();
                                    mode = 1;
                                }
                                else
                                    Echo(pass1 + " " + pass2 + " " + pass3);
                            }
                        }
                    }
                    else if (info[0] == "antennapos" && info.Length == 2)
                    {
                        Boolean updated = Vector3D.TryParse(info[1], out antennaPos);
                        if (updated)
                            antenna.Radius = (float)(Vector3D.Distance(rc.GetPosition(), antennaPos) + 10);
                        else
                            Echo("Failed to update antenna position");
                    }
                    else if (mode == 2 && !rc.IsAutoPilotEnabled && Vector3D.Distance(rc.GetPosition(), rc.CurrentWaypoint.Coords) >= 5)
                        rc.SetAutoPilotEnabled(true);
                    else if (mode == 1 && globalPath.Count != 0)
                        FollowPath(globalPath, true);
                }
                else if (mode == 1)
                    FollowPath(globalPath, true);
                else if (mode == 2 && rc.IsAutoPilotEnabled && Vector3D.Distance(rc.GetPosition(), rc.CurrentWaypoint.Coords) < 5)
                {
                    Dock3();
                    Boolean sent = antenna.TransmitMessage("freepath," + groupName);
                    if (!sent) messageQue.Add("freepath," + groupName);
                }
                else if (mode == 2 && !rc.IsAutoPilotEnabled && Vector3D.Distance(rc.GetPosition(), rc.CurrentWaypoint.Coords) >= 5)
                    rc.SetAutoPilotEnabled(true);
                else if (mode == 3 && Dock3())
                    Echo("DOCKED!");
                else if (mode == 6)
                    FollowPath(reversePath, false); 
                else if (updateSource == UpdateType.Update100)
                    shipMass = rc.CalculateShipMass().TotalMass;

                if (waiting)
                    Echo("Waiting for clearance");
            }
            else
                Echo("SETUP FAILED. DO NOT SETUP WHILE CONNECTED TO ANOTHER GRID");
        }

        void EnableRC(List<MyWaypointInfo> inList, out List<MyWaypointInfo> outList)
        {
            rc.ClearWaypoints();
            List<MyWaypointInfo> tempList = new List<MyWaypointInfo>();
            foreach (MyWaypointInfo point in inList)
                tempList.Add(point);

            outList = tempList;

            rc.SetDockingMode(true);
            rc.SetCollisionAvoidance(true);
            rc.SetAutoPilotEnabled(true);
            rc.FlightMode = FlightMode.OneWay;
        }

        void FollowPath(List<MyWaypointInfo> path, Boolean goingIn)
        {
            if (path.Count == 1 && !goingIn && lastReversePath)
            {
                Vector3D lastPathCoords = path.First().Coords;
                Vector3D unitVector = Vector3D.Normalize(lastPathCoords - rc.GetPosition());
                Vector3D finalCoords = lastPathCoords + (unitVector * finalOutDistance);
                path.Clear();
                rc.ClearWaypoints();
                MyWaypointInfo finalWaypoint = new MyWaypointInfo("FINAL", finalCoords);
                path.Add(finalWaypoint);
                rc.SetCollisionAvoidance(true);
                lastReversePath = false;
            }
            else if (path.Count != 0)
            {
                if (Vector3D.Distance(rc.GetPosition(), path.First().Coords) > 8)
                {
                    Echo("FollowPath: " + Vector3D.Distance(rc.GetPosition(), path.First().Coords));
                    if (!rc.IsAutoPilotEnabled)
                    {
                        rc.ClearWaypoints();
                        rc.AddWaypoint(path.First());
                        rc.SetCollisionAvoidance(collisionAvoidance);
                        rc.SetDockingMode(false);
                        rc.SetAutoPilotEnabled(true);
                    }
                    else if (Vector3D.Distance(rc.GetPosition(), rc.CurrentWaypoint.Coords) < 5)
                    {
                        path.Remove(path.First());
                        collisionAvoidance = false;
                        rc.ClearWaypoints();
                    }
                }
                else
                {
                    path.Remove(path.First());
                    collisionAvoidance = false;
                    rc.ClearWaypoints();
                }
            }
            else if (goingIn) 
                Dock2(dockingConnectorPos, dockingDir);
            else
            {
                Storage = "";
                dockingConnectorPos = new Vector3D();
                dockingDir = new Vector3D();
                mode = 0;
                globalPath.Clear();
                reversePath.Clear();
                rc.ClearWaypoints();
                lastReversePath = true;

                foreach (IMyThrust thrust in allThrusters)
                {
                    thrust.ThrustOverridePercentage = 0;
                }
                foreach (IMyGyro gyro in gyros)
                {
                    gyro.GyroOverride = false;
                    gyro.Pitch = 0;
                    gyro.Yaw = 0;
                    gyro.Roll = 0;
                }

                Boolean sent = antenna.TransmitMessage("freepath," + groupName);
                if (!sent) messageQue.Add("freepath," + groupName);
                groupName = "default";

                //any ending commands here
            }
        }

        void Dock2(Vector3D dockingConnectorPos, Vector3D dockingDir)
        {
            mode = 2;
            rc.ClearWaypoints();
            collisionAvoidance = true;

            Vector3D initDockPoint = dockingConnectorPos + (dockingDir * lengthOfShip);
            MyWaypointInfo dockPoint = new MyWaypointInfo("Final Dock Point", initDockPoint);
            rc.SetDockingMode(false);
            rc.SetCollisionAvoidance(collisionAvoidance);
            rc.FlightMode = FlightMode.OneWay;
            rc.AddWaypoint(dockPoint);
            rc.Direction = Base6Directions.Direction.Forward;
            rc.SpeedLimit = 10;
            rc.SetAutoPilotEnabled(true);

            try
            {
                if (Vector3D.Distance(rc.CurrentWaypoint.Coords, rc.GetPosition()) < 15)
                {
                    rc.SetAutoPilotEnabled(false);
                    Boolean sent = antenna.TransmitMessage("freepath," + groupName);
                    if (!sent) messageQue.Add("freepath," + groupName);
                    Dock3();
                }
            }
            catch { Echo("WHEEW CAUGHT THAT"); }
        }

        Boolean Dock3()
        {
            mode = 3;

            foreach (IMyThrust thruster in allThrusters)
                thruster.ThrustOverridePercentage = 0;

            rc.SetAutoPilotEnabled(false);
            foreach (IMyGyro gyro in gyros)
                gyro.ApplyAction("OnOff_On");

            double yaw;
            double pitch;

            Boolean yawLock = false;
            Boolean pitchLock = false;

            GetRotationAngles(Vector3D.Negate(dockingDir), connector, out yaw, out pitch);
            ApplyGyroOverride(pitch, yaw, 0, gyros, connector);

            if (yaw < 0.01)
                yawLock = true;
            if (pitch < 0.01)
                pitchLock = true;

            Echo("yawLock:" + yawLock);
            Echo("pitchLock" + pitchLock);

            if (pitchLock && yawLock)
            {
                Vector3D closestPoint;
                double distanceFromDockingVector = DistanceFromVector(dockingConnectorPos, dockingDir, connector.GetPosition(), out closestPoint);

                Echo("Distance from docking vector:" + distanceFromDockingVector);

                if (distanceFromDockingVector > 0.35)
                {
                    NewAdjustTest(closestPoint, connector, distanceFromDockingVector);
                }
                else if (distanceFromDockingVector == -1)
                    Echo("Error in docking vector distance calculation");
                else
                {
                    float shipmass = rc.CalculateShipMass().TotalMass;
                    if (rc.GetShipSpeed() < 1.5)
                        foreach (IMyThrust thruster in backThrusterGroup)
                            thruster.ThrustOverride = shipmass;
                    else
                        foreach (IMyThrust thruster in backThrusterGroup)
                            thruster.ThrustOverridePercentage = 0;

                    if (Vector3D.Distance(dockingConnectorPos, rc.GetPosition()) > errorDistance)
                    {
                        Storage = "";
                        dockingConnectorPos = new Vector3D();
                        dockingDir = new Vector3D();
                        mode = 0;
                        globalPath.Clear();
                        rc.ClearWaypoints();
                        foreach (IMyThrust thrust in allThrusters)
                            thrust.ThrustOverridePercentage = 0;

                        foreach (IMyGyro gyro in gyros)
                        {
                            gyro.GyroOverride = false;
                            gyro.Pitch = 0;
                            gyro.Yaw = 0;
                            gyro.Roll = 0;
                        }

                        Boolean sent = antenna.TransmitMessage("canceldock," + password);
                        if (!sent)
                            messageQue.Add("canceldock," + password);
                        antenna.CustomName += " ERROR";
                    }
                }
            }

            if (connector.Status == MyShipConnectorStatus.Connectable)
            {
                connector.Connect(); mode = 5;
                globalPath.Clear();

                foreach (IMyThrust thruster in allThrusters)
                    thruster.ThrustOverridePercentage = 0;

                foreach (IMyGyro gyro in gyros)
                {
                    gyro.Pitch = 0;
                    gyro.Yaw = 0;
                    gyro.Roll = 0;
                }
                return true;
            }
            return false;
        }

        //THANKS WHIPLASH!!
        void GetRotationAngles(Vector3D targetVector, IMyTerminalBlock reference, out double yaw, out double pitch)
        {
            var localTargetVector = Vector3D.TransformNormal(targetVector, MatrixD.Transpose(reference.WorldMatrix));
            var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);

            yaw = VectorAngleBetween(Vector3D.Forward, flattenedTargetVector) * Math.Sign(localTargetVector.X); //right is positive
            if (Math.Abs(yaw) < 1E-6 && localTargetVector.Z > 0) //check for straight back case
                yaw = Math.PI;

            if (Vector3D.IsZero(flattenedTargetVector)) //check for straight up case
                pitch = MathHelper.PiOver2 * Math.Sign(localTargetVector.Y);
            else
                pitch = VectorAngleBetween(localTargetVector, flattenedTargetVector) * Math.Sign(localTargetVector.Y); //up is positive
        }

        double VectorAngleBetween(Vector3D a, Vector3D b) //returns radians 
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                return 0;
            else
                return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
        }

        void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, IMyTerminalBlock reference)
        {
            var rotationVec = new Vector3D(-pitch_speed, yaw_speed, roll_speed); //because keen does some weird stuff with signs
            var shipMatrix = reference.WorldMatrix;
            var relativeRotationVec = Vector3D.TransformNormal(rotationVec, shipMatrix);
            foreach (var thisGyro in gyro_list)
            {
                var gyroMatrix = thisGyro.WorldMatrix;
                var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(gyroMatrix));
                thisGyro.Pitch = (float)transformedRotationVec.X;
                thisGyro.Yaw = (float)transformedRotationVec.Y;
                thisGyro.Roll = (float)transformedRotationVec.Z;
                thisGyro.GyroOverride = true;
            }
        }

        public double DistanceFromVector(Vector3D origin, Vector3D direction, Vector3D currentPos, out Vector3D closestPoint)
        {
            direction.Normalize();
            Vector3D lhs = currentPos - origin;

            double dotP = lhs.Dot(direction);
            closestPoint = origin + direction * dotP;
            return Vector3D.Distance(currentPos, closestPoint);
        }

        void NewAdjustTest(Vector3D closestPoint, IMyFunctionalBlock reference, double distanceFromVectorPoint)
        {
            foreach (IMyThrust thruster in backThrusterGroup) thruster.ThrustOverridePercentage = 0f;
            closestPoint -= reference.GetPosition(); //converting to relative coords;

            Vector3D leftDir = new Vector3D();
            Vector3D rightDir = new Vector3D();
            Vector3D upDir = new Vector3D();
            Vector3D downDir = new Vector3D();

            foreach (IMyThrust thruster in allThrusters)
            {
                if (thruster.Orientation.Forward == Base6Directions.GetFlippedDirection(reference.Orientation.Up)) downThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == reference.Orientation.Up) upThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == reference.Orientation.Left) leftThrusterGroup.Add(thruster);
                else if (thruster.Orientation.Forward == Base6Directions.GetFlippedDirection(reference.Orientation.Left)) rightThrusterGroup.Add(thruster);
            }

            leftDir = leftThrusterGroup.First().WorldMatrix.Forward;
            rightDir = rightThrusterGroup.First().WorldMatrix.Forward;
            upDir = upThrusterGroup.First().WorldMatrix.Forward;
            downDir = downThrusterGroup.First().WorldMatrix.Forward;

            Echo("Distance From Docking Vector: " + distanceFromVectorPoint);

            double leftDot = leftDir.Dot(closestPoint);
            double rightDot = rightDir.Dot(closestPoint);
            double upDot = upDir.Dot(closestPoint);
            double downDot = downDir.Dot(closestPoint);

            if (leftDot < rightDot)
                if (Math.Abs(leftDot) > 0.1)
                    ApplyProperThrust(leftThrusterGroup, rightThrusterGroup, leftDir, leftDot, closestPoint, distanceFromVectorPoint);
            else
                if (Math.Abs(rightDot) > 0.1)
                    ApplyProperThrust(rightThrusterGroup, leftThrusterGroup, rightDir, rightDot, closestPoint, distanceFromVectorPoint);

            if (upDot < downDot)
                if (Math.Abs(upDot) > 0.1)
                    ApplyProperThrust(upThrusterGroup, downThrusterGroup, upDir, upDot, closestPoint, distanceFromVectorPoint);

            else
                if (Math.Abs(downDot) > 0.1)
                    ApplyProperThrust(downThrusterGroup, upThrusterGroup, downDir, downDot, closestPoint, distanceFromVectorPoint);
        }

        void ApplyProperThrust(List<IMyThrust> thrustGroup, List<IMyThrust> opposingThrustGroup, Vector3D dir, double dotProduct, Vector3D closestPoint, double distanceFromPoint)
        {
            if (rc.GetShipSpeed() < 0.2 && distanceFromPoint < 3) 
                foreach (IMyThrust thruster in thrustGroup)
                    thruster.ThrustOverridePercentage = 1f; 
            else if (rc.GetShipSpeed() < 1 && distanceFromPoint < 8) 
                foreach (IMyThrust thruster in thrustGroup)
                    thruster.ThrustOverridePercentage = 1f; 
            else if (rc.GetShipSpeed() < 3 && distanceFromPoint >= 8)
                foreach (IMyThrust thruster in thrustGroup)
                    thruster.ThrustOverridePercentage = 1f;
            else 
                foreach (IMyThrust thruster in thrustGroup)
                    thruster.ThrustOverridePercentage = 0f;

            foreach (IMyThrust thruster in opposingThrustGroup)
                thruster.ThrustOverridePercentage = 0f;
        }

        void CheckMessageQue()
        {
            if (messageQue.Count != 0)
            {
                Boolean sent = antenna.TransmitMessage(messageQue.First());
                if (sent) {
                    Echo("REMOVING: " + messageQue.First());
                    messageQue.Remove(messageQue.First());
                }
            }
        }
    }
}