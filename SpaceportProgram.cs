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
        string nameOfSpaceport = "Test Port";
        int freeConnectorTime = 300;

        IMyRadioAntenna antenna;
        IMyTextPanel lcd;
        IMyRemoteControl rc;

        MyIni ini = new MyIni();

        List<Connector> defaultConnectorList = new List<Connector>();

        Dictionary<string, List<Connector>> connectorGroupDict = new Dictionary<string, List<Connector>>();
        Dictionary<string, List<MyWaypointInfo>> connectorGroupPath = new Dictionary<string, List<MyWaypointInfo>>();
        Dictionary<string, Boolean> pathFreeDict = new Dictionary<string, bool>();

        List<QuePos> dockingQue = new List<QuePos>();
        List<string> messageQue = new List<string>();
        List<string> argumentQue = new List<string>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            antenna = GridTerminalSystem.GetBlockWithName("Antenna") as IMyRadioAntenna;
            lcd = GridTerminalSystem.GetBlockWithName("lcd") as IMyTextPanel;
            rc = GridTerminalSystem.GetBlockWithName("rc spaceport") as IMyRemoteControl;

            connectorGroupDict.Add("default", defaultConnectorList);

            ini.TryParse(Storage);

            int i = 0;
            messageQue.Clear();
            while (ini.ContainsKey("save", "messageQue_" + i))
            {
                string element = ini.Get("save", "messageQue_" + i).ToString();
                messageQue.Add(element);
                i++;
            }

            i = 0;
            argumentQue.Clear();
            while (ini.ContainsKey("save", "argumentQue_" + i))
            {
                string element = ini.Get("save", "argumentQue_" + i).ToString();
                argumentQue.Add(element);
                i++;
            }

            i = 0;
            dockingQue.Clear();
            while (ini.ContainsKey("save", "dockingQue_" + i))
            {
                QuePos element = new QuePos("ERROR");
                Boolean success = QuePos.TryParse(ini.Get("save", "dockingQue_" + i).ToString(), out element);
                if (success) dockingQue.Add(element);
                //else add to debug panel
                i++;
            }

            i = 0;
            connectorGroupDict.Clear();
            while (ini.ContainsKey("save", "connectorGroupDict_" + i))
            {
                string str = ini.Get("save", "connectorGroupDict_" + i).ToString();
                string[] info = str.Split(new string[] { "|" }, StringSplitOptions.None);
                string key = info[0];
                string[] subInfo = info[1].Split(new string[] { ":" }, StringSplitOptions.None);
                foreach (string data in subInfo)
                {
                    try
                    {
                        string[] subData = data.Split(new string[] { ";" }, StringSplitOptions.None);
                        string customName = subData[0];
                        Boolean free; Boolean freeSuccess = Boolean.TryParse(subData[1], out free);
                        int freeCounter; Boolean freeCounterSuccess = Int32.TryParse(subData[2], out freeCounter);
                        string reservedPassword = subData[3];

                        if (freeSuccess && freeCounterSuccess)
                            AddConnector(customName, key, free, freeCounter, reservedPassword);
                        else
                        {
                            //add info to debug panel
                        }
                    }
                    catch
                    {
                        //add info to debug panel
                    }
                }
                i++;
            }

            i = 0;
            connectorGroupPath.Clear();
            while (ini.ContainsKey("save", "connectorGroupPath_" + i))
            {
                string str = ini.Get("save", "connectorGroupPath_" + i).ToString();
                string[] info = str.Split(new string[] { "|" }, StringSplitOptions.None);
                string key = info[0];
                string[] subInfo = info[1].Split(new string[] { ";" }, StringSplitOptions.None);
                List<MyWaypointInfo> waypointInfos = new List<MyWaypointInfo>();
                foreach (string data in subInfo)
                {
                    try
                    {
                        MyWaypointInfo newWaypoint = new MyWaypointInfo();
                        Boolean success = MyWaypointInfo.TryParse(data, out newWaypoint);
                        if (success)
                            waypointInfos.Add(newWaypoint);
                        else 
                            lcd.WritePublicText("\nFAILED DATA (E): " + data, true);
                    }
                    catch
                    {
                        lcd.WritePublicText("\nFAILED DATA (C): " + data, true);
                    }
                }
                SetGroupPath(key, waypointInfos);
                i++;
            }

        }

        public void Save()
        {
            ini.Clear();

            for (var j = 0; j < messageQue.Count; j++)
                ini.Set("save", "messageQue_" + j, messageQue[j]);
            for (var k = 0; k < argumentQue.Count; k++)
                ini.Set("save", "argumentQue_" + k, argumentQue[k]);
            for (var m = 0; m < dockingQue.Count; m++)
                ini.Set("save", "dockingQue_" + m, dockingQue[m].ToString());

            int i = 0;
            foreach(string key in connectorGroupDict.Keys)
            {
                string connectorInfos = key + "|";
                foreach (Connector connector in connectorGroupDict[key])
                {
                    connectorInfos += connector.instance.CustomName + ";" + connector.free + ";"
                        + connector.freeCounter + ";" + connector.reservedPassword + ":";
                }
                ini.Set("save", "connectorGroupDict_" + i, connectorInfos);
                i++;
            }

            i = 0;
            foreach(string key in connectorGroupPath.Keys)
            {
                string waypointInfos = key + "|";
                foreach (MyWaypointInfo waypoint in connectorGroupPath[key])
                {
                    waypointInfos += waypoint.ToString() + ";";
                }
                ini.Set("save", "connectorGroupPath_" + i, waypointInfos);
                i++;
            }

            Storage = ini.ToString();

        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (rc.GetShipSpeed() < 1)
            {
                if (!String.IsNullOrEmpty(argument))
                {
                    if (argument.Substring(argument.Length - 1, 1) == ",") argument = argument.Remove(argument.LastIndexOf(','), 1);
                    string[] info = argument.Split(new string[] { "," }, StringSplitOptions.None);
                    if (info[0].ToLower() == "add")
                    {
                        if (info.Length == 2)
                        {
                            if (!string.IsNullOrEmpty(info[1]))
                                AddConnector(info[1]);
                            else
                                WriteError(argument);
                        }
                        else if (info.Length == 3)
                        {
                            if (!string.IsNullOrEmpty(info[1]) && !string.IsNullOrEmpty(info[2]))
                                AddConnector(info[1], info[2]);
                            else
                                WriteError(argument);
                        }
                        else
                            WriteError(argument); 

                    }

                    else if (info[0].ToLower() == "addbytag")
                    {
                        if (info.Length == 2)
                            AddConnectedByTag(info[1]);
                        else if (info.Length == 3)
                            AddConnectedByTag(info[1], info[2]);
                        else
                            WriteError(argument);
                    }

                    else if (info[0].ToLower() == "remove")
                    {
                        if (info.Length == 2)
                            RemoveConnector(info[1]);
                        else
                            WriteError(argument);
                    }

                    else if (info[0].ToLower() == "removegroup")
                    {
                        try {
                            RemoveGroup(info[1]);
                            Echo("Removed group: " + info[1]); }
                        catch {
                            WriteError(argument);
                        }
                    }
                    else if (info[0].ToLower() == "free")
                    {
                        if (info.Length == 2)
                            FreeConnector(info[1]);
                        else
                            WriteError(argument);
                    }

                    else if (info[0].ToLower() == "showallgroups")
                    {
                        ShowAllGroups();
                        Echo("Check designated LCD");
                    }

                    else if (info[0].ToLower() == "requestdock") //0 command, 1 password, 2 group (if exist), 3 override
                    {
                        Echo("Docking info requested with length: " + info.Length);
                        lcd.WritePublicText("\nDocking Arg:" + argument, true);
                        string password = info[1];

                        Boolean duplicate = false;
                        Boolean leaving = false;

                        if (info.Length != 4)
                            foreach (QuePos request in dockingQue)
                                if (request.password == password)
                                    duplicate = true;  
                        else
                            if (info[3] == "leaving")
                                    leaving = true;

                        lcd.WritePublicText("\nDuplicate: " + duplicate, true);

                        if (!duplicate)
                        {
                            Connector connector = null;
                            Boolean good = true;

                            if (info.Length == 2)
                                connector = GetFreeConnector(password, leaving);
                            else if (info.Length > 2)
                                connector = GetFreeConnector(info[2], password, leaving);
                            else
                                WriteError(argument); good = false;

                            Boolean sent = antenna.TransmitMessage(password + ",received," + nameOfSpaceport);
                            if (!sent) messageQue.Add(password + ",received," + nameOfSpaceport);
                            Echo((connector == null) + "");

                            if (connector != null || leaving)
                            {
                                if (leaving)
                                    foreach (List<Connector> list in connectorGroupDict.Values)
                                    {
                                        connector = list.First();
                                        break;
                                    }

                                lcd.WritePublicText("\nFound Connector", true);
                                connector.reservedPassword = password;
                                Echo("Re?");
                                Boolean hasPath = false; List<MyWaypointInfo> path = new List<MyWaypointInfo>();

                                string groupName = "";

                                if (info.Length > 2) {
                                    groupName = info[2];
                                    hasPath = connectorGroupPath.ContainsKey(info[2]);
                                    if (hasPath)
                                        path = connectorGroupPath[info[2]];
                                }
                                else if (info.Length == 2) {
                                    groupName = "default";
                                    hasPath = connectorGroupPath.ContainsKey("default");
                                    if (hasPath)
                                        path = connectorGroupPath["default"];
                                }

                                Echo("hasPath:" + hasPath);
                                if (!hasPath) SendDockingInfo(connector, password);
                                else
                                {
                                    if (pathFreeDict.ContainsKey(groupName))
                                    {
                                        if (pathFreeDict[groupName])
                                        {
                                            pathFreeDict[groupName] = false;
                                            SendDockingInfo(connector, password, TranslateToWorldCoords(path, rc));
                                            foreach (MyWaypointInfo waypoint in TranslateToWorldCoords(path, rc))
                                                lcd.WritePublicText("\nName: " + waypoint.Name + " Coord: " + waypoint.Coords, true);
                                        }
                                        else
                                        {
                                            sent = antenna.TransmitMessage(password + ",waitque");
                                            if (!sent)
                                                messageQue.Add(password + ",waitque");

                                            QuePos newQue = new QuePos(null, null);
                                            if (info.Length == 2)
                                                newQue = new QuePos(password, "default");
                                            if (info.Length == 3)
                                                newQue = new QuePos(password, info[2]);

                                            if (newQue.password != null && newQue.group != null)
                                                dockingQue.Add(newQue);
                                        }
                                    }
                                    else
                                    {
                                        WriteError("pathFreeDict KEY WAS NOT FOUND");
                                    }
                                }
                                Echo("A");
                            }
                            else if (good)
                            {
                                lcd.WritePublicText("\nDidn't find a free connector", true);
                                sent = antenna.TransmitMessage(password + ",waitque");
                                if (!sent)
                                    messageQue.Add(password + ",waitque");

                                QuePos newQue = new QuePos(null, null);
                                if (info.Length == 2)
                                    newQue = new QuePos(password, "default");
                                if (info.Length == 3)
                                    newQue = new QuePos(password, info[2]);

                                if (newQue.password != null && newQue.group != null)
                                    dockingQue.Add(newQue);
                            }
                            else {
                                sent = antenna.TransmitMessage(password + ",fail," + nameOfSpaceport);
                                if (!sent)
                                    messageQue.Add(password + ",fail," + nameOfSpaceport);
                            }
                        }
                        else
                        {
                            Boolean sent = antenna.TransmitMessage(password + "reject");
                            if (!sent)
                                messageQue.Add(password + "reject");
                        }
                    }

                    else if (info[0].ToLower() == "freepath")
                    {
                        if (info.Length == 1)
                            pathFreeDict["default"] = true;
                        else if (info.Length == 2)
                            pathFreeDict[info[1]] = true;
                        else
                            WriteError(argument);

                        lcd.WritePublicText("freepath arg: " + argument);
                    }

                    else if (info[0].ToLower() == "canceldock")
                    {
                        if (info.Length == 2)
                        {
                            string password = info[1];
                            List<QuePos> removeList = new List<QuePos>();
                            foreach (QuePos que in dockingQue)
                                if (que.password == password)
                                    removeList.Add(que);

                            foreach (QuePos removeQue in removeList)
                                dockingQue.Remove(removeQue);

                            foreach (List<Connector> list in connectorGroupDict.Values)
                                foreach (Connector connector in list)
                                    if (connector.reservedPassword == password)
                                    {
                                        connector.free = true;
                                        connector.freeCounter = 0;
                                        connector.reservedPassword = "";
                                    }
                        }
                    }

                    else if (info[0].ToLower() == "setpath") //0 command, 1 groupname, 2+ waypoints
                    {
                        if (info.Length > 2)
                        {
                            Echo("info length: " + info.Length);
                            string group = info[1];
                            string[] sublist = new string[info.Length - 2];
                            Echo("sub length: " + info.Length);
                            for (int i = 0; i < sublist.Length; i++)
                                sublist[i] = info[i + 2];

                            foreach (string str in sublist) {
                                if (!String.IsNullOrEmpty(str)) {
                                    Echo(str);
                                    lcd.WritePublicText("\nstr: " + str, true);
                                }
                                else {
                                    Echo("EMPTY STR ERROR");
                                }
                            }

                            lcd.WritePublicText("\nRC coords" + rc.GetPosition() + "\n", true);

                            List<MyWaypointInfo> waypointInfos = StringArraytoWaypoint(sublist);

                            lcd.WritePublicText("\n", true);

                            try
                            {
                                foreach (MyWaypointInfo waypoint in waypointInfos)
                                    lcd.WritePublicText("\nName: " + waypoint.Name + " Coord: " + waypoint.Coords, true);

                                if (waypointInfos != null)
                                {
                                    List<MyWaypointInfo> relativeWaypointInfos = TranslateToRelativeCoords(waypointInfos, rc);
                                    SetGroupPath(group, relativeWaypointInfos);
                                    Echo("Path set for: " + group);
                                }
                                else
                                    WriteError(argument);
                            }
                            catch
                            {
                                WriteError(argument);
                            }
                        }
                        else
                            WriteError(argument);
                    }

                    else if (info[0].ToLower() == "cleardata")
                    {
                        Storage = "";
                        connectorGroupDict.Clear();
                        connectorGroupPath.Clear();
                        pathFreeDict.Clear();
                        messageQue.Clear();
                        dockingQue.Clear();
                        argumentQue.Clear();

                        Echo("DATA CLEARED");
                    }

                    else if (info[0].ToLower() == "debug")
                        Debug();
                }

                if (updateSource != UpdateType.Trigger) {
                    CheckQue();
                    CheckMessageQue();
                }

                if (messageQue.Count > 0)
                    Echo("First Que: " + messageQue.First());
                else {
                    Echo("No messages in que");
                    SendAntennaPos();
                }

                UpdateFreeConnectors();
            }
            else if (updateSource == UpdateType.Antenna)
                argumentQue.Add(argument);
            else
                Echo("SHIP MOVING. SPACEPORT OFFLINE.");
        }

        void AddConnector(string nameOfConnector)
        { //adds a connector to the default group
            IMyShipConnector newShipConnector = GridTerminalSystem.GetBlockWithName(nameOfConnector) as IMyShipConnector;
            Connector newConnector = new Connector(newShipConnector);
            if (newConnector != null && newShipConnector != null)
            {
                Boolean duplicate = false;
                foreach (Connector connector in connectorGroupDict["default"])
                    if (connector.instance.CustomName == newConnector.instance.CustomName) {
                        duplicate = true;
                        Echo(nameOfConnector + " is a duplicate");
                        break;
                    }

                if (!duplicate) {
                    newConnector.instance.PullStrength = float.PositiveInfinity;
                    connectorGroupDict["default"].Add(newConnector);
                    Echo(nameOfConnector + " was added");
                }
            }
            else
                Echo("No connector named '" + nameOfConnector + "' found.");
        }

        void AddConnector(string nameOfConnector, string nameOfGroup) //adds a connector to the given list, and adds a new list if it doesn't exist.
        {
            if (!connectorGroupDict.ContainsKey(nameOfGroup))
            {
                connectorGroupDict.Add(nameOfGroup, new List<Connector>());
                Echo("New connector group '" + nameOfGroup + "' added.");
            }
            IMyShipConnector newShipConnector = GridTerminalSystem.GetBlockWithName(nameOfConnector) as IMyShipConnector;
            Connector newConnector = new Connector(newShipConnector);
            if (newConnector != null && newShipConnector != null)
            {
                Boolean duplicate = false;
                foreach (Connector connector in connectorGroupDict[nameOfGroup])
                    if (connector.instance.CustomName == newConnector.instance.CustomName) {
                        duplicate = true;
                        Echo(nameOfConnector + " is a duplicate");
                        break;
                    }
                if (!duplicate) {
                    newConnector.instance.PullStrength = float.PositiveInfinity;
                    connectorGroupDict[nameOfGroup].Add(newConnector);
                    Echo(nameOfConnector + " was added to: " + nameOfGroup);
                }
            }
            else
                Echo("No connector named '" + nameOfConnector + "' found.");
        }

        void AddConnector(string nameOfConnector, string nameOfGroup, Boolean free, int freeCounter, string reservedPassword) //adds a connector to the given list, and adds a new list if it doesn't exist.
        {
            if (!connectorGroupDict.ContainsKey(nameOfGroup))
            {
                connectorGroupDict.Add(nameOfGroup, new List<Connector>());
                Echo("New connector group '" + nameOfGroup + "' added.");
            }

            IMyShipConnector newShipConnector = GridTerminalSystem.GetBlockWithName(nameOfConnector) as IMyShipConnector;
            Connector newConnector = new Connector(newShipConnector);
            if (newConnector != null && newShipConnector != null)
            {
                Boolean duplicate = false;
                foreach (Connector connector in connectorGroupDict[nameOfGroup])
                    if (connector.instance.CustomName == newConnector.instance.CustomName) {
                        duplicate = true;
                        Echo(nameOfConnector + " is a duplicate");
                        break;
                    }
                if (!duplicate) {
                    newConnector.instance.PullStrength = float.PositiveInfinity;
                    newConnector.free = free; newConnector.freeCounter = freeCounter;
                    newConnector.reservedPassword = reservedPassword;
                    connectorGroupDict[nameOfGroup].Add(newConnector);
                    Echo(nameOfConnector + " was added to: " + nameOfGroup);
                }
            }
            else
                Echo("No connector named '" + nameOfConnector + "' found.");
        }

        void AddConnectedByTag(string tag)
        {
            List<IMyShipConnector> allConnectors = new List<IMyShipConnector>(); GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(allConnectors);
            int i = 0;
            foreach (IMyShipConnector connector in allConnectors)
                if (connector.CustomName.Contains(tag))
                {
                    AddConnector(connector.CustomName, "default");
                    i++;
                }
            Echo("Added " + i + " connectors.");
        }

        void AddConnectedByTag(string tag, string nameOfGroup)
        {
            List<IMyShipConnector> allConnectors = new List<IMyShipConnector>(); GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(allConnectors);
            int i = 0;
            foreach (IMyShipConnector connector in allConnectors)
                if (connector.CustomName.Contains(tag))
                {
                    AddConnector(connector.CustomName, nameOfGroup);
                    i++;
                }
            Echo("Added " + i + " connectors.");
        }

        void RemoveConnector(string nameOfConnector)
        {
            Connector connectorToRemove = null;
            foreach (List<Connector> list in connectorGroupDict.Values)
            {
                foreach (Connector connector in list)
                    if (connector == null) { Echo("NULL"); }
                    else
                    {
                        if (connector.instance.CustomName == nameOfConnector)
                            connectorToRemove = connector;
                    }
                if (connectorToRemove != null) {
                    list.Remove(connectorToRemove);
                    Echo("Removed " + nameOfConnector + " from a group");
                }
            }
            if (connectorToRemove == null)
                Echo("No connector named '" + nameOfConnector + "' was found.");
        }

        void RemoveGroup(string nameOfGroup)
        { //removes group from dictionary
            if (connectorGroupDict.ContainsKey(nameOfGroup)) {
                connectorGroupDict.Remove(nameOfGroup);
                Echo(nameOfGroup + "was successfully removed!");
            }
            else
                Echo(nameOfGroup + " does not exist");
        }

        void FreeConnector(string nameOfConnector)
        {
            Connector connectorToFree = null;
            foreach (List<Connector> list in connectorGroupDict.Values)
            {
                foreach (Connector connector in list)
                {
                    if (connector == null)
                        Echo("NULL");
                    else if (connector.instance.CustomName == nameOfConnector)
                        connectorToFree = connector;
                }
                if (connectorToFree != null) {
                    connectorToFree.free = true;
                    connectorToFree.freeCounter = 0 ;
                    Echo("Freed " + nameOfConnector + " from a group");
                }
            }
            if (connectorToFree == null)
                Echo("No connector named '" + nameOfConnector + "' was found.");
        }

        void ShowAllGroups()
        { //displays all groups
            lcd.WritePublicText("CaptXan's Spaceport Manager \n");
            lcd.WritePublicText("---------------------------\n", true);
            foreach (String groupName in connectorGroupDict.Keys)
            {
                lcd.WritePublicText(groupName + " - Size: " + connectorGroupDict[groupName].Count
                    + " Custom Path: " + connectorGroupPath.ContainsKey(groupName) + "\n", true);
                foreach (Connector connector in connectorGroupDict[groupName])
                    lcd.WritePublicText("  " + connector.instance.CustomName + " : Free = " + connector.free + "\n", true);
            }
        }

        void SendDockingInfo(Connector connector, string password)
        {
            string message = password + ",dockinginfo," + connector.instance.CustomName + ","
                + connector.instance.GetPosition() + "," + connector.instance.WorldMatrix.Forward;
            lcd.WritePublicText(message);
            Boolean sent = antenna.TransmitMessage(message);
            if (!sent) messageQue.Add(message);

            connector.reservedPassword = password;
        }

        void SendDockingInfo(Connector connector, string password, List<MyWaypointInfo> waypointInfos)
        {
            string strWaypoints = "";
            foreach (MyWaypointInfo waypoint in waypointInfos)
            {
                strWaypoints += waypoint.ToString() + ",";
            }
            try
            {
                if (strWaypoints.Substring(strWaypoints.Length - 1, 1) == ",")
                    strWaypoints = strWaypoints.Remove(strWaypoints.LastIndexOf(','), 1);
            }
            catch
            {
                lcd.WritePublicText("ERROR: "+strWaypoints);
            }

           Boolean sent = antenna.TransmitMessage(password + ",dockinginfo," + connector.instance.CustomName + ","
                + connector.instance.GetPosition() + "," + connector.instance.WorldMatrix.Forward + "," + strWaypoints);
            if (!sent) messageQue.Add(password + ",dockinginfo," + connector.instance.CustomName + ","
                + connector.instance.GetPosition() + "," + connector.instance.WorldMatrix.Forward + "," + strWaypoints);

            connector.reservedPassword = password;
        }

        Connector GetFreeConnector(string password, Boolean leaving)
        {
            foreach (Connector connector in connectorGroupDict["default"])
                if (connector.free || connector.reservedPassword == password)
                {
                    if (!leaving)
                    {
                        connector.free = false;
                        connector.reservedPassword = password;
                    }
                    return connector;
                }
            return null;
        }

        Connector GetFreeConnector(string group, string password, Boolean leaving)
        {
            if (group == "anywhere")
                foreach(List<Connector> list in connectorGroupDict.Values)
                    foreach(Connector connector in list)
                    {
                        if (connector.free || connector.reservedPassword == password)
                        {
                            if (!leaving)
                            {
                                connector.free = false;
                                connector.reservedPassword = password;
                            }
                            return connector;
                        }
                    }
            else
            {
                foreach (Connector connector in connectorGroupDict[group])
                    if (connector.free || connector.reservedPassword == password)
                    {
                        connector.free = false;
                        connector.reservedPassword = password;
                        return connector;
                    }
            }
            return null;
        }

        List<MyWaypointInfo> StringArraytoWaypoint(string[] strList)
        {
            List<MyWaypointInfo> waypointList = new List<MyWaypointInfo>();

            foreach (string coord in strList)
            {
                Echo(coord);
                MyWaypointInfo newWaypointInfo;
                Boolean passed = MyWaypointInfo.TryParse(coord, out newWaypointInfo);

                if (!passed)
                    return null;

                waypointList.Add(newWaypointInfo);
            }

            return waypointList;
        }

        List<MyWaypointInfo> TranslateToRelativeCoords(List<MyWaypointInfo> list, IMyRemoteControl reference)
        {
            List<MyWaypointInfo> newList = new List<MyWaypointInfo>();
            Boolean one = list == null;
            Boolean two = reference == null;
            Echo(one + " ... " + two);

            int i = 1;
            foreach (MyWaypointInfo point in list)
            {
                Vector3D referenceWorldPosition = reference.WorldMatrix.Translation;

                Vector3D worldDirection = point.Coords - referenceWorldPosition;

                Vector3D bodyPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(reference.WorldMatrix));
                MyWaypointInfo newPoint = new MyWaypointInfo("" + i, bodyPosition);
                newList.Add(newPoint);
                i++;
            }
            return newList;
        }

        List<MyWaypointInfo> TranslateToWorldCoords(List<MyWaypointInfo> list, IMyRemoteControl reference)
        {
            List<MyWaypointInfo> newList = new List<MyWaypointInfo>();
            int i = 1;
            foreach (MyWaypointInfo point in list)
            {
                Vector3D worldPosition = Vector3D.Transform(point.Coords, reference.WorldMatrix);
                MyWaypointInfo newPoint = new MyWaypointInfo(i + "", worldPosition);
                newList.Add(newPoint);
                i++;
            }
            return newList;
        }

        void SetGroupPath(string group, List<MyWaypointInfo> pathPoints)
        {
            if (!connectorGroupPath.ContainsKey(group))
            {
                connectorGroupPath.Add(group, pathPoints);
                pathFreeDict.Add(group, true);
                Echo("Pathpoints for '" + group + "' created.");
            }
            else
            {
                connectorGroupPath[group] = pathPoints;
                Echo("Pathpoints for '" + group + "' overridden.");
            }
        }

        void WriteError(string command)
        {
            Echo("Error detected, check LCD");
            lcd.WritePublicText("ERROR:\n");
            lcd.WritePublicText(command + "\n", true);
            lcd.WritePublicText("INCORRECT FORMAT\n", true);
        }

        void CheckQue()
        {
            List<QuePos> removeRequestList = new List<QuePos>();
            foreach (QuePos request in dockingQue)
            {
                string group = request.group;
                if (pathFreeDict[group])
                {
                    foreach (Connector connector in connectorGroupDict[group])
                    {
                        Boolean doBreak = false;

                        if ((connector.reservedPassword == request.password || connector.free) && connector.instance.Status == MyShipConnectorStatus.Unconnected)
                        {
                            Main("requestdock," + request.password + "," + group + ",override", UpdateType.Trigger);
                            removeRequestList.Add(request); doBreak = true;
                        }

                        if (doBreak) {
                            pathFreeDict[group] = false;
                            break;
                        }
                    }
                }
            }
            foreach(QuePos request in removeRequestList)
                dockingQue.Remove(request);
        }

        void CheckMessageQue()
        {
            if (messageQue.Count != 0)
            {
                Boolean sent = antenna.TransmitMessage(messageQue.First());
                if (sent)
                    messageQue.Remove(messageQue.First());
            }
        }

        void CheckArgumentQue()
        {
            if (argumentQue.Count != 0)
            {
                string argument = argumentQue.First();
                argumentQue.Remove(argumentQue.First());
                Main(argument, UpdateType.Mod);
            }
        }

        void UpdateFreeConnectors()
        {
            Echo("Updating Free Connectors");
            foreach(List<Connector> list in connectorGroupDict.Values)
            {
                foreach (Connector connector in list)
                {
                    Echo("Updating Connector: " + connector.instance.CustomName + "\n\tConnected: " + (connector.instance.Status == MyShipConnectorStatus.Connected));
                    if (!connector.free && connector.instance.Status == MyShipConnectorStatus.Unconnected) {
                        connector.freeCounter++;
                        Echo(connector.instance.CustomName + " - Time to dock: " + (freeConnectorTime - connector.freeCounter)); //Timer until a connector is freed
                    } 
                    else if (connector.free && connector.instance.Status == MyShipConnectorStatus.Connected) {
                        connector.free = false;
                        connector.freeCounter = freeConnectorTime - 10; //If a connector is connected, make sure it aint free
                    }

                    if (connector.freeCounter > freeConnectorTime && connector.instance.Status == MyShipConnectorStatus.Unconnected)
                    {
                        connector.free = true; connector.freeCounter = 0;
                        Boolean sent = antenna.TransmitMessage(connector.reservedPassword + ",rejected");
                        if (!sent)
                            messageQue.Add(connector.reservedPassword + ",rejected");
                        connector.reservedPassword = "";
                    }
                }
            }
        }

        void Debug()
        {
            lcd.WritePublicText("DEBUG: ");
            foreach(string key in pathFreeDict.Keys)
                lcd.WritePublicText("\n"+key + ":" + pathFreeDict[key], true);

            lcd.WritePublicText("\nDocking Que : " + dockingQue.Count, true);

            foreach (QuePos que in dockingQue)
                lcd.WritePublicText("\nPass: " + que.password + " group: " + que.group, true);
        }

        void SendAntennaPos()
        {
            messageQue.Add("antennapos," + antenna.GetPosition());
        }

        public class Connector
        {
            public IMyShipConnector instance;
            public bool free = true;
            public int freeCounter = 0;
            public string reservedPassword = "";

            public Connector(IMyShipConnector connector)
            {
                instance = connector;
            }
        }
        
        public class QuePos
        {
            public string password;
            public string group = "default";

            public QuePos(string pass, string g)
            {
                password = pass;
                group = g;
            }
            public QuePos(string pass)
            {
                password = pass;
            }

            public override String ToString()
            {
                return password + ":" + group;
            }

            public static Boolean TryParse(string str, out QuePos quePos)
            {
                try
                {
                    string[] info = str.Split(new string[] { ":" }, StringSplitOptions.None);
                    if (info.Length == 2)
                    {
                        QuePos newQuePos = new QuePos(info[0], info[1]);
                        quePos = newQuePos;
                        return true;
                    }
                    else
                    {
                        quePos = null;
                        return false;
                    }
                }
                catch
                {
                    quePos = null;
                    return false;
                }

            }
        }
    }
}