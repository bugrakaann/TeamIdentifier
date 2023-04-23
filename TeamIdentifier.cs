// Requires: ImageLibrary
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using System.Globalization;
using Oxide.Core;
using Oxide.Core.Configuration;
using System.Collections;
using ConVar;
using Facepunch;
using Facepunch.Math;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Description("Identifies the player team when player connected")]
    [Info("TeamIdentifier","Apwned","1.0.0")]
    internal class TeamIdentifier : RustPlugin
    {
        [PluginReference] private Plugin ImageLibrary;
        #region Fields
        const string teampanelui = "teamidentifier.teampanel.ui";
        const string scoreboardui = "teamidentifier.scoreboard.ui";
        internal RelationshipManager.PlayerTeam GreenTeam;
        internal RelationshipManager.PlayerTeam RedTeam;
        internal RelationshipManager.PlayerTeam BlueTeam;
        internal RelationshipManager.PlayerTeam OrangeTeam;
        Timer playerchecker;

        public SpawnPoints spawnpoints;
        public static GroupData groupData = new GroupData();
        public Dictionary<ulong,DateTime> disconnectedPlayers = new Dictionary<ulong,DateTime>();
        #endregion
        #region Configuration
        public ConfigData configdata;
        DynamicConfigFile groupdatafile;
        DynamicConfigFile disconnectedplayersfile;
        public class ConfigData
        {
            [JsonProperty(PropertyName ="Red Spawn Point (in Vector3 format)")]
            public string redspawn = string.Empty;

            [JsonProperty(PropertyName = "Orange Spawn Point")]
            public string orangespawn = string.Empty;

            [JsonProperty(PropertyName = "Blue Spawn Point")]
            public string bluespawn = string.Empty;

            [JsonProperty(PropertyName = "Green Spawn Point")]
            public string greenspawn = string.Empty;

            [JsonProperty(PropertyName = "Time interval for ability of changing teams(In hours)")]
            public double changeteaminterval = 1;

            [JsonProperty(PropertyName = "Time interval of kicking player after disconnecting(In hours)")]
            public double kickplayerduration = 12;
            [JsonProperty(PropertyName = "Disconnected players check interval(Used for kicking timed out players)(In seconds)")]
            public float playercheckinterval = 900f;
            [JsonProperty(PropertyName = "Debug Chat Conversations to servers console")]
            public bool debug = false;
            [JsonProperty(PropertyName = "Show Scoreboard")]
            public bool showscoreboard = true;
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            configdata = Config.ReadObject<ConfigData>();
            Config.WriteObject(configdata, true);
        }

        protected override void LoadDefaultConfig()
        {
            configdata = new ConfigData();
        }
        protected override void SaveConfig() => Config.WriteObject(configdata, true);

        public class GroupData
        {
            public Group RedTeam = new Group();
            public Group BlueTeam = new Group();
            public Group GreenTeam = new Group();
            public Group OrangeTeam = new Group();

            public class Group
            {
                public List<ulong> players = new List<ulong>();
                public int killCount { get; set; } = 0;
            }
        }
        void SaveGroupData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/GroupData",groupData);
        }
        void ReadGroupData()
        {
            groupdatafile = Interface.Oxide.DataFileSystem.GetFile($"{Name}/GroupData");

            try
            {
                groupData = groupdatafile.ReadObject<GroupData>();
            }
            catch
            {

            }
        }
        void SaveDisconnectedPlayersData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/DisconnectedPlayers", disconnectedPlayers);
        }
        void ReadDisconnectedPlayersData()
        {
            disconnectedplayersfile = Interface.Oxide.DataFileSystem.GetFile($"{Name}/DisconnectedPlayers");

            try
            {
                disconnectedPlayers = disconnectedplayersfile.ReadObject<Dictionary<ulong,DateTime>>();
            }
            catch
            {
                PrintError("Error");
            }
        }
        #endregion
        #region Hooks
        void Init()
        {
            ReadGroupData();
            ReadDisconnectedPlayersData();
        }
        void Unload()
        {
            SaveGroupData();
            SaveDisconnectedPlayersData();
            playerchecker?.Destroy();
        }
        void OnServerInitialized(bool initial)
        {
            BasePlayer.activePlayerList.ToList().ForEach(x => x.ClearTeam());
            RelationshipManager.ServerInstance.playerToTeam.Clear();
            RelationshipManager.ServerInstance.teams.Clear();

            GreenTeam = RelationshipManager.ServerInstance.CreateTeam();
            GreenTeam.teamName = "Green";
            RedTeam = RelationshipManager.ServerInstance.CreateTeam();
            RedTeam.teamName = "Red";
            BlueTeam = RelationshipManager.ServerInstance.CreateTeam();
            BlueTeam.teamName = "Blue";
            OrangeTeam = RelationshipManager.ServerInstance.CreateTeam();
            OrangeTeam.teamName = "Orange";

            RelationshipManager.maxTeamSize = 25;
            if (string.IsNullOrEmpty(configdata.greenspawn) || string.IsNullOrEmpty(configdata.orangespawn) || string.IsNullOrEmpty(configdata.redspawn) || string.IsNullOrEmpty(configdata.bluespawn))
            {
                PrintError("Default spawn points loaded please check config and reload the plugin");
                return;
            }
            spawnpoints = new SpawnPoints()
            {
                BlueSpawnPoint = StringToVector3(configdata.bluespawn),
                GreenSpawnPoint = StringToVector3(configdata.greenspawn),
                OrangeSpawnPoint = StringToVector3(configdata.orangespawn),
                RedSpawnPoint = StringToVector3(configdata.redspawn)
            };

            BlueTeam.MarkDirty();
            GreenTeam.MarkDirty();
            RedTeam.MarkDirty();
            OrangeTeam.MarkDirty();

            LoadAllGroups();
            CheckPlayers();
            playerchecker =timer.Every(configdata.playercheckinterval, CheckPlayers);
            RegisterImages();
        }
        private void OnPlayerConnected(BasePlayer player)
        {
            disconnectedPlayers.Remove(player.userID);
            if(groupData.BlueTeam.players.Contains(player.userID))
            {
                player.ChatMessage($"<color=yellow>Automatically joined Blue team</color>");
                RemoveFromTeam(player);
                ChangeTeam(player, TeamType.Blue);
                return;
            }
            else if (groupData.RedTeam.players.Contains(player.userID))
            {
                player.ChatMessage($"<color=yellow>Automatically joined Red team</color>");
                RemoveFromTeam(player);
                ChangeTeam(player, TeamType.Red);
                return;
            }
            else if (groupData.GreenTeam.players.Contains(player.userID))
            {
                player.ChatMessage($"<color=yellow>Automatically joined Green team</color>");
                RemoveFromTeam(player);
                ChangeTeam(player, TeamType.Green);
                return;
            }
            else if (groupData.OrangeTeam.players.Contains(player.userID))
            {
                player.ChatMessage($"<color=yellow>Automatically joined Orange team</color>");
                RemoveFromTeam(player);
                ChangeTeam(player, TeamType.Orange);
                return;
            }
            if (CheckSpawnPoints())
                SendSelectTeamUI(player);

            SendScoreboardUI(player);
        }
        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            disconnectedPlayers.Add(player.userID,DateTime.Now);
        }
        object OnTeamCreate(BasePlayer player)
        {
            // Player is never able to create team
            return false;
        }
        object OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer player, ulong target)
        {
            return false;
        }
        object OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            return false;
        }
        object OnEntityTakeDamage(DecayEntity entity, HitInfo info)
        {
            BasePlayer attacker = info.InitiatorPlayer;
            BasePlayer entityowner = BasePlayer.FindByID(entity.OwnerID);
            BuildingPrivlidge privlidge = entity.GetBuildingPrivilege();

            if (privlidge == null)
                return null;

            if (entityowner == null || attacker == entityowner || attacker.IsAdmin || privlidge.authorizedPlayers.Any(x => x.userid == attacker.userID))
                return null;

            if (groupData.OrangeTeam.players.Contains(attacker.userID) && groupData.OrangeTeam.players.Contains(entityowner.userID))
                info.damageTypes.ScaleAll(0f);
            else if (groupData.RedTeam.players.Contains(attacker.userID) && groupData.RedTeam.players.Contains(entityowner.userID))
                info.damageTypes.ScaleAll(0f);
            else if (groupData.BlueTeam.players.Contains(attacker.userID) && groupData.BlueTeam.players.Contains(entityowner.userID))
                info.damageTypes.ScaleAll(0f);
            else if (groupData.GreenTeam.players.Contains(attacker.userID) && groupData.GreenTeam.players.Contains(entityowner.userID))
                info.damageTypes.ScaleAll(0f);
            return null;
        }
        object OnPlayerRespawn(BasePlayer player)
        {
            if (player.Team == GreenTeam && CheckSpawnPoints())
                return new BasePlayer.SpawnPoint() { pos = spawnpoints.GreenSpawnPoint, rot = new Quaternion(0, 0, 0, 1) };
            else if (player.Team == RedTeam && CheckSpawnPoints())
                return new BasePlayer.SpawnPoint() { pos = spawnpoints.RedSpawnPoint, rot = new Quaternion(0, 0, 0, 1) };
            else if (player.Team == BlueTeam && CheckSpawnPoints())
                return new BasePlayer.SpawnPoint() { pos = spawnpoints.BlueSpawnPoint, rot = new Quaternion(0, 0, 0, 1) };
            else if (player.Team == OrangeTeam && CheckSpawnPoints())
                return new BasePlayer.SpawnPoint() { pos = spawnpoints.OrangeSpawnPoint, rot = new Quaternion(0, 0, 0, 1) };

            return null;
        }
        object OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            //No friendly fire
            BasePlayer victim = info.HitEntity as BasePlayer;

            if (victim == null)
                return null;

            if (groupData.OrangeTeam.players.Contains(attacker.userID) && groupData.OrangeTeam.players.Contains(victim.userID))
                return false;
            else if (groupData.RedTeam.players.Contains(attacker.userID) && groupData.RedTeam.players.Contains(victim.userID))
                return false;
            else if (groupData.BlueTeam.players.Contains(attacker.userID) && groupData.BlueTeam.players.Contains(victim.userID))
                return false;
            else if (groupData.GreenTeam.players.Contains(attacker.userID) && groupData.GreenTeam.players.Contains(victim.userID))
                return false;
            return null;
        }
        object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            BasePlayer attacker = info?.InitiatorPlayer;
            if (attacker == null || !(attacker is BasePlayer) || !(player is BasePlayer) || info == null || attacker == player)
                return null;

            if (groupData.BlueTeam.players.Contains(attacker.userID))
            {
                groupData.BlueTeam.killCount++;
                RefreshScoreboard();
            }
            else if (groupData.RedTeam.players.Contains(attacker.userID))
            {
                groupData.RedTeam.killCount++;
                RefreshScoreboard();
            }
            else if (groupData.OrangeTeam.players.Contains(attacker.userID))
            {
                groupData.OrangeTeam.killCount++;
                RefreshScoreboard();
            }
            else if (groupData.GreenTeam.players.Contains(attacker.userID))
            {
                groupData.GreenTeam.killCount++;
                RefreshScoreboard();
            }

            return null;
        }
        object OnTeamDisband(RelationshipManager.PlayerTeam team)
        {
            return false;
        }
        //object OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
        //{
        //    if(player.Team == GreenTeam)
        //    {
        //        SendChatMessage(player, "<color=green>", "[Green]" + "</color>", player.displayName + ":", message,channel);
        //        return false;
        //    }
        //    else if (player.Team == BlueTeam)
        //    {
        //        SendChatMessage(player, "<color=#6495ED>", "[Blue]" + "</color>", player.displayName + ":", message, channel);
        //        return false;
        //    }
        //    else if (player.Team == OrangeTeam)
        //    {
        //        SendChatMessage(player, "<color=orange>", "[Orange]" + "</color>", player.displayName + ":", message, channel);
        //        return false;
        //    }
        //    else if (player.Team == RedTeam)
        //    {
        //        SendChatMessage(player, "<color=red>", "[Red]" + "</color>", player.displayName + ":", message, channel);
        //        return false;
        //    }

        //    return null;
        //}
        private object OnBetterChat(Dictionary<string, object> data)
        {
            BasePlayer player = data["BasePlayer"] as BasePlayer;
            if (player == null)
                return null;
            string username = data["Username"] as string;

            if (player.Team == GreenTeam)
            {
                data["Username"] = "<color=green>[Green] </color>" + username;
                return data;
            }
            else if (player.Team == BlueTeam)
            {
                data["Username"] = "<color=#6495ED>[Blue] </color>" + username;
                return data;
            }
            else if (player.Team == OrangeTeam)
            {
                data["Username"] = "<color=orange>[Orange] </color>" + username;
                return data;
            }
            else if (player.Team == RedTeam)
            {
                data["Username"] = "<color=red>[Red] </color>" + username;
                return data;
            }
            return null;
        }
        #endregion
        #region Helpers
        internal void AddImage(string imageName, string url) => ImageLibrary.Call("AddImage", url, imageName, 0UL, null);
        internal string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name, 0UL, false);
        private void RegisterImages()
        {
            AddImage("scoreboard", "https://www.dropbox.com/s/7tbj4xctxqb05fl/Scoreboardtemplate.png?dl=1");
        }
        public static Vector3 StringToVector3(string sVector)
        {
            // Remove the parentheses
            if (sVector.StartsWith("(") && sVector.EndsWith(")"))
            {
                sVector = sVector.Substring(1, sVector.Length - 2);
            }

            // split the items
            string[] sArray = sVector.Split(',');

            // store as a Vector3
            Vector3 result = new Vector3(
                float.Parse(sArray[0]),
                float.Parse(sArray[1]),
                float.Parse(sArray[2]));

            return result;
        }
        public bool CheckSpawnPoints()
        {
            if(string.IsNullOrEmpty(configdata.greenspawn) || string.IsNullOrEmpty(configdata.bluespawn) || string.IsNullOrEmpty(configdata.orangespawn) || string.IsNullOrEmpty(configdata.redspawn))
            {
                return false;
            }
            return true;
        }
        public void CheckDuplicateTeamData()
        {
            List<ulong> Ids = new List<ulong>();
            Ids.AddRange(groupData.GreenTeam.players);
            Ids.AddRange(groupData.BlueTeam.players);
            Ids.AddRange(groupData.RedTeam.players);
            Ids.AddRange(groupData.OrangeTeam.players);

            IEnumerable<ulong> duplicates = Ids.GroupBy(x => x)
                .SelectMany(g => g.Skip(1));
        }
        // <summary>
        /// Teleport player to the specified position
        /// </summary>
        /// <param name="player"></param>
        /// <param name="destination"></param>
        /// <param name="sleep"></param>
        internal void MovePosition(BasePlayer player, TeamType teamtype, bool sleep)
        {
            Vector3 destination = new Vector3();
            switch (teamtype)
            {
                case TeamType.Green:
                    destination = spawnpoints.GreenSpawnPoint;
                    break;
                case TeamType.Red:
                    destination = spawnpoints.RedSpawnPoint;
                    break;
                case TeamType.Blue:
                    destination = spawnpoints.BlueSpawnPoint;
                    break;
                case TeamType.Orange:
                    destination = spawnpoints.OrangeSpawnPoint;
                    break;
                default:
                    PrintError("error occured");
                    break;
            }

            if (player == null)
                return;
            if (player.isMounted)
                player.GetMounted().DismountPlayer(player, true);

            if (player.GetParentEntity() != null)
                player.SetParent(null);

            if (sleep)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.MovePosition(destination);
                player.UpdateNetworkGroup();
                player.StartSleeping();
                player.SendNetworkUpdateImmediate(false);
                player.ClearEntityQueue(null);
                player.ClientRPCPlayer(null, player, "StartLoading");
                player.SendFullSnapshot();
            }
            else
            {
                player.MovePosition(destination);
                player.ClientRPCPlayer(null, player, "ForcePositionTo", destination);
                player.SendNetworkUpdateImmediate();
                player.ClearEntityQueue(null);
            }
        }
        private void CheckPlayers()
        {
            foreach(ulong player in disconnectedPlayers.Keys)
            {
                if(disconnectedPlayers[player].AddHours(configdata.kickplayerduration) < DateTime.Now)
                {
                    RelationshipManager.PlayerTeam team = FindTeamByUserID(player);
                    team?.RemovePlayer(player);
                    if (groupData.GreenTeam.players.Contains(player))
                    {
                        groupData.GreenTeam.players.Remove(player);
                        GreenTeam.RemovePlayer(player);
                    }
                    else if (groupData.RedTeam.players.Contains(player))
                    {
                        groupData.RedTeam.players.Remove(player);
                        RedTeam.RemovePlayer(player);
                    }
                    else if (groupData.BlueTeam.players.Contains(player))
                    {
                        groupData.BlueTeam.players.Remove(player);
                        BlueTeam.RemovePlayer(player);
                    }
                    else if (groupData.OrangeTeam.players.Contains(player))
                    {
                        groupData.OrangeTeam.players.Remove(player);
                        OrangeTeam.RemovePlayer(player);
                    }
                    disconnectedPlayers.Remove(player);
                }
            }
        }
        void RemoveFromTeam(BasePlayer player)
        {
            if (player.currentTeam == 0UL)
            {
                return;
            }
            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
            team?.RemovePlayer(player.userID);
            
            if(groupData.GreenTeam.players.Contains(player.userID))
                groupData.GreenTeam.players.Remove(player.userID);
            else if(groupData.BlueTeam.players.Contains(player.userID))
                groupData.BlueTeam.players.Remove(player.userID);
            else if (groupData.RedTeam.players.Contains(player.userID))
                groupData.RedTeam.players.Remove(player.userID);
            else if (groupData.OrangeTeam.players.Contains(player.userID))
                groupData.OrangeTeam.players.Remove(player.userID);
        }
        void ChangeTeam(BasePlayer player, TeamType teamType)
        {
            switch (teamType)
            {
                case TeamType.Green:
                    if (!RelationshipManager.ServerInstance.teams.ContainsKey(GreenTeam.teamID))
                    {
                        GreenTeam = RelationshipManager.ServerInstance.CreateTeam();
                    }

                    if (GreenTeam.members.Contains(player.userID) || groupData.GreenTeam.players.Contains(player.userID))
                        return;

                    if (GreenTeam.teamLeader == 0)
                        GreenTeam.SetTeamLeader(player.userID);
                    GreenTeam.AddPlayer(player);

                    groupData.GreenTeam.players.Add(player.userID);

                    GreenTeam.MarkDirty();
                    player.ChatMessage("<color=yellow>Green team selected</color>");
                    break;

                case TeamType.Red:

                    if (!RelationshipManager.ServerInstance.teams.ContainsKey(RedTeam.teamID))
                    {
                        RedTeam = RelationshipManager.ServerInstance.CreateTeam();
                        RedTeam.SetTeamLeader(player.userID);
                    }

                    if (RedTeam.members.Contains(player.userID) || groupData.RedTeam.players.Contains(player.userID))
                        return;

                    if (RedTeam.teamLeader == 0)
                        RedTeam.SetTeamLeader(player.userID);
                    RedTeam.AddPlayer(player);

                    groupData.RedTeam.players.Add(player.userID);

                    RedTeam.MarkDirty();
                    player.ChatMessage("<color=yellow>Red team selected</color>");
                    break;
                case TeamType.Blue:
                    
                    if (!RelationshipManager.ServerInstance.teams.ContainsKey(BlueTeam.teamID))
                    {
                        BlueTeam = RelationshipManager.ServerInstance.CreateTeam();
                        BlueTeam.SetTeamLeader(player.userID);
                    }
                        

                    if (BlueTeam.members.Contains(player.userID) || groupData.BlueTeam.players.Contains(player.userID))
                        return;

                    if (BlueTeam.teamLeader == 0)
                        BlueTeam.SetTeamLeader(player.userID);
                    BlueTeam.AddPlayer(player);

                    groupData.BlueTeam.players.Add(player.userID);

                    BlueTeam.MarkDirty();
                    player.ChatMessage("<color=yellow>Blue team selected</color>");
                    break;
                case TeamType.Orange:

                    if (!RelationshipManager.ServerInstance.teams.ContainsKey(OrangeTeam.teamID))
                    {
                        OrangeTeam = RelationshipManager.ServerInstance.CreateTeam();
                        OrangeTeam.SetTeamLeader(player.userID);
                    }
                        

                    if (OrangeTeam.members.Contains(player.userID) || groupData.OrangeTeam.players.Contains(player.userID))
                        return;

                    if (OrangeTeam.teamLeader == 0)
                        OrangeTeam.SetTeamLeader(player.userID);
                    OrangeTeam.AddPlayer(player);

                    groupData.OrangeTeam.players.Add(player.userID);

                    OrangeTeam.MarkDirty();
                    player.ChatMessage("<color=yellow>Orange team selected</color>");
                    break;
                default:
                    System.Console.WriteLine("Error occured");
                    break;
            }
        }

        RelationshipManager.PlayerTeam FindTeamByUserID(ulong userID)
        {
            if (GreenTeam.members.Contains(userID))
                return GreenTeam;
            else if (RedTeam.members.Contains(userID))
                return RedTeam;
            else if (BlueTeam.members.Contains(userID))
                return BlueTeam;
            else if (OrangeTeam.members.Contains(userID))
                return OrangeTeam;
            return null;
        }
        
        bool IsTeamFull(TeamType teamtype)
        {
            switch (teamtype)
            {
                case TeamType.Green:
                    if (GreenTeam?.members?.Count == 25)
                        return true;
                    return false;
                case TeamType.Red:
                    if (RedTeam?.members?.Count == 25)
                        return true;
                    return false;
                case TeamType.Blue:
                    if (BlueTeam?.members?.Count == 25)
                        return true;
                    return false;
                case TeamType.Orange:
                    if (OrangeTeam?.members?.Count == 25)
                        return true;
                    return false;
                default:
                    PrintError("Error occured");
                    return false;
            }
        }
        [ConsoleCommand("kickfromteam")]
        void LeaveTeam(ConsoleSystem.Arg args)
        {
            if (args.Player() != null )
                return;

            string player = args.GetString(0);
            if (!args.HasArgs(0))
                return;
            BasePlayer baseplayer = BasePlayer.Find(player);

            if(baseplayer != null)
            {
                if(baseplayer.currentTeam == 0)
                {
                    Puts("Player has no team");
                    return;
                }
                RemoveFromTeam(baseplayer);
                Puts(baseplayer.displayName + " kicked from his team successfully");
                baseplayer.ChatMessage("<color=yellow>You have been kicked from your team by admin</color>");
                return;
            }
            ulong playerID= args.GetULong(0);
            BasePlayer baseplayer2 = BasePlayer.FindByID(playerID);
            if(baseplayer2 != null)
            {
                if (baseplayer2.currentTeam == 0)
                {
                    Puts("Player has no team");
                    return;
                }
                RemoveFromTeam(baseplayer2);
                Puts(baseplayer2.displayName + " kicked from his team successfully");
                return;
            }
            if(groupData.RedTeam.players.Contains(playerID))
            {
                groupData.RedTeam.players.Remove(playerID); 
                Puts(playerID + " kicked from his team successfully");
                return;
            }
            else if (groupData.BlueTeam.players.Contains(playerID))
            {
                groupData.BlueTeam.players.Remove(playerID);
                Puts(playerID + " kicked from his team successfully");
                return;
            }
            else if (groupData.GreenTeam.players.Contains(playerID))
            {
                groupData.GreenTeam.players.Remove(playerID);
                Puts(playerID + " kicked from his team successfully");
                return;
            }
            else if (groupData.OrangeTeam.players.Contains(playerID))
            {
                groupData.OrangeTeam.players.Remove(playerID);
                Puts(playerID + " kicked from his team successfully");
                return;
            }
            Puts("Player is not found. Enter player steamID or name");
        }

        int GetOnlinePlayerNum(TeamType type)
        {
            int active = 0;
            switch (type)
            {
                case TeamType.Green:
                    foreach(ulong player in groupData.GreenTeam.players)
                    {
                        BasePlayer basePlayer = BasePlayer.FindByID(player);
                        if (basePlayer == null)
                            continue;
                        if(BasePlayer.activePlayerList.Contains(basePlayer))
                            active++;
                    }
                    return active;
                case TeamType.Red:
                    foreach (ulong player in groupData.RedTeam.players)
                    {
                        BasePlayer basePlayer = BasePlayer.FindByID(player);
                        if (basePlayer == null)
                            continue;
                        if (BasePlayer.activePlayerList.Contains(basePlayer))
                            active++;
                    }
                    return active;
                case TeamType.Blue:
                    foreach (ulong player in groupData.BlueTeam.players)
                    {
                        BasePlayer basePlayer = BasePlayer.FindByID(player);
                        if (basePlayer == null)
                            continue;
                        if (BasePlayer.activePlayerList.Contains(basePlayer))
                            active++;
                    }
                    return active;
                case TeamType.Orange:
                    foreach (ulong player in groupData.OrangeTeam.players)
                    {
                        BasePlayer basePlayer = BasePlayer.FindByID(player);
                        if (basePlayer == null)
                            continue;
                        if (BasePlayer.activePlayerList.Contains(basePlayer))
                            active++;
                    }
                    return active;
            }
            return -1;
        }

        bool LoadAllGroups()
        {
            try
            {
                foreach (ulong member in groupData.GreenTeam.players)
                {
                    BasePlayer player = BasePlayer.FindByID(member);
                    if (player == null)
                        continue;
                    GreenTeam.AddPlayer(player);
                }
                foreach (ulong member in groupData.BlueTeam.players)
                {
                    BasePlayer player = BasePlayer.FindByID(member);
                    if (player == null)
                        continue;
                    BlueTeam.AddPlayer(player);
                }
                foreach (ulong member in groupData.RedTeam.players)
                {
                    BasePlayer player = BasePlayer.FindByID(member);
                    if (player == null)
                        continue;
                    RedTeam.AddPlayer(player);
                }
                foreach (ulong member in groupData.OrangeTeam.players)
                {
                    BasePlayer player = BasePlayer.FindByID(member);
                    if (player == null)
                        continue;
                    OrangeTeam.AddPlayer(player);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        /*
        private object SendChatMessage(BasePlayer player, string pColor, string prefix, string displayname, string message, Chat.ChatChannel channel)
        {
            RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
            {
                Channel = channel,
                Message = new System.Text.RegularExpressions.Regex("<[^>]*>").Replace(string.Join(" ", message), ""),
                UserId = player.IPlayer.Id,
                Username = player.displayName,
                Color = pColor,
                Time = Epoch.Current
            });

            switch ((int)channel)
            {
                // global chat
                case 0:
                    if (configdata.debug) Puts("default / global chat");

                    var gMsg = ArrayPool.Get(3);
                    gMsg[0] = (int)channel;
                    gMsg[1] = player.UserIDString;

                    foreach (BasePlayer p in BasePlayer.activePlayerList.Where(p => p.IsValid() == true))
                    {
                        gMsg[2] = $"{pColor}{prefix} {displayname} {message}";
                        p.SendConsoleCommand("chat.add", gMsg);
                        if (configdata.debug) Puts("sended GLOBAL message (" + message + ") to " + p.displayName);
                    }
                    ArrayPool.Free(gMsg);
                    break;

                // team channel
                case 1:
                    if (configdata.debug) Puts("team chat");

                    var tMsg = ArrayPool.Get(3);
                    tMsg[0] = (int)channel;
                    tMsg[1] = player.UserIDString;

                    foreach (BasePlayer p in BasePlayer.activePlayerList.Where(p => p.Team != null && player.Team != null && p.Team.teamID == player.Team.teamID && p.IsValid() == true))
                    {
                        tMsg[2] = $"{pColor}{prefix} {displayname} {message}";
                        p.SendConsoleCommand("chat.add", tMsg);
                        if (configdata.debug) Puts("sended GLOBAL message (" + message + ") to " + p.displayName);
                    }
                    ArrayPool.Free(tMsg);
                    break;

                default:
                    break;
            }
            return true;
        }
        */
        #endregion
        #region Chat Commands
        [ChatCommand("help")]
        void cmdGetHelp(BasePlayer player)
        {
            player.ChatMessage("<size=20><b><color=yellow>Help</color></b></size>\n<size=16>/team info \n/team join <color=#6495ED>blue</color>\n/team join <color=green>green</color>\n/team join <color=red>red</color>\n/team join <color=orange>orange</color>\n/score</size>");
            if (player.IsAdmin)
            {
                player.ChatMessage("<size=20><b><color=yellow>Admin Commands</color></b></size>\n" +
                    "<size=16>/getposition\n" +
                    "kickfromteam <steamID/name> (Console Command)\n" +
                    "resetscores (Console Command)</size>");
            }
        }
        [ConsoleCommand("resetscores")]
        void cmdResetScores(ConsoleSystem.Arg args)
        {
            if(args.Player() == null || args.Player().IsAdmin)
            {
                groupData.GreenTeam.killCount = 0;
                groupData.RedTeam.killCount = 0;
                groupData.OrangeTeam.killCount = 0;
                groupData.BlueTeam.killCount = 0;
                SaveGroupData();
                if (args.Player() == null)
                    Puts("Scores has been reset");
                RefreshScoreboard();
            }
        }
        [ChatCommand("team")]
        void cmdTeam(BasePlayer player, string command, string[] arg)
        {
            if (arg == null || arg.Length == 0)
            {
                player.ChatMessage("Invalid command");
                return;
            }
            if ((arg[0] != "info" && arg[0] != "join"))
            {
                player.ChatMessage("Invalid command");
                return;
            }

            if (arg[0] == "info")
            {
                player.ChatMessage($"<size=20><b><color=yellow>Team Info</color></b></size>\n" +
                    $"<size=16><color=green>Green</color> ({GetOnlinePlayerNum(TeamType.Green)} players online)\n" +
                    $"<color=#6495ED>Blue</color> ({GetOnlinePlayerNum(TeamType.Blue)} players online)\n" +
                    $"<color=red>Red</color> ({GetOnlinePlayerNum(TeamType.Red)} players online)\n" +
                    $"<color=orange>Orange</color> ({GetOnlinePlayerNum(TeamType.Orange)} players online)</size>");
            }

            else if (arg[0] == "join")
            {
                if (SaveRestore.SaveCreatedTime.AddHours(configdata.changeteaminterval) < DateTime.Now && player.currentTeam != 0)
                {
                    player.ChatMessage($"<color=yellow>You can only change teams within the first {configdata.changeteaminterval} hours of wipe</color>");
                    return;
                }
                if (arg[1] == "green")
                {
                    RemoveFromTeam(player);
                    ChangeTeam(player, TeamType.Green);
                    MovePosition(player, TeamType.Green, player.IsSleeping());
                    player.ChatMessage("You entered <color=green>green</color> team");
                }
                else if (arg[1] == "red")
                {
                    RemoveFromTeam(player);
                    ChangeTeam(player, TeamType.Red);
                    MovePosition(player, TeamType.Red, player.IsSleeping());
                    player.ChatMessage("You entered <color=red>red</color> team");
                }
                else if (arg[1] == "blue")
                {
                    RemoveFromTeam(player);
                    ChangeTeam(player, TeamType.Blue);
                    MovePosition(player, TeamType.Blue, player.IsSleeping());
                    player.ChatMessage("You entered <color=#6495ED>blue</color> team");
                }
                else if (arg[1] == "orange")
                {
                    RemoveFromTeam(player);
                    ChangeTeam(player, TeamType.Orange);
                    MovePosition(player, TeamType.Orange, player.IsSleeping());
                    player.ChatMessage("You entered <color=orange>orange</color> team");
                }
            }
        }
        [ChatCommand("score")]
        void cmdScore(BasePlayer player)
        {
            player.ChatMessage($"<size=20><b><color=yellow>Scores</color></b></size>\n" +
                $"<size=16><color=green>Green ({groupData.GreenTeam.killCount} kills)</color> \n" +
                $"<color=#6495ED>Blue ({groupData.BlueTeam.killCount} kills)</color> \n" +
                $"<color=red>Red ({groupData.RedTeam.killCount} kills)</color> \n" +
                $"<color=orange>Orange ({groupData.OrangeTeam.killCount} kills)</color> </size>");
        }
        [ChatCommand("getposition")]
        void cmdGetpos(BasePlayer player)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("Invalid command");
                return;
            }
            Puts("Your current location:" + player.ServerPosition.ToString());
        }
        #endregion
        #region UI
        void SendSelectTeamUI(BasePlayer player)
        {

            const string redpanelui = "teamidentifier.redpanel";
            const string greenpanelui = "teamidentifier.greenpanel";
            const string bluepanelui = "teamidentifier.bluepanel";
            const string orangepanelui = "teamidentifier.orangepanel";

            CuiElementContainer container = UI.Container(teampanelui, UI.Color("#303030", 1f), UI.TransformToUI4(518f, 1402f, 264f, 719f), true);
            UI.Label(container, teampanelui, "Select Your Team", 22, UI.TransformToUI4(5f, 880f, 378f, 423f, 884f, 455f), TextAnchor.MiddleCenter);

            UI.Button(container, teampanelui, UI.Color("#ff0000", 1f), string.Empty, 1, UI.TransformToUI4(28f, 228f, 52f, 341f, 884f, 455f), "teamidentifier.select red", TextAnchor.MiddleCenter, redpanelui);
            UI.Button(container, teampanelui, UI.Color("#04dd14", 1f), string.Empty, 1, UI.TransformToUI4(235f, 435f, 52f, 341f, 884f, 455f), "teamidentifier.select green", TextAnchor.MiddleCenter, greenpanelui);
            UI.Button(container, teampanelui, UI.Color("#6495ED", 1f), string.Empty, 1, UI.TransformToUI4(442f, 642f, 52f, 341f, 884f, 455f), "teamidentifier.select blue", TextAnchor.MiddleCenter, bluepanelui);
            UI.Button(container, teampanelui, UI.Color("#ffa500", 1f), string.Empty, 1, UI.TransformToUI4(649f, 849f, 52f, 341f, 884f, 455f), "teamidentifier.select orange", TextAnchor.MiddleCenter, orangepanelui);

            UI.Label(container, redpanelui, "Red", 17, UI.TransformToUI4(5f, 195f, 230f, 260f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");
            UI.Label(container, greenpanelui, "Green", 17, UI.TransformToUI4(5f, 195f, 230f, 260f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");
            UI.Label(container, bluepanelui, "Blue", 17, UI.TransformToUI4(5f, 195f, 230f, 260f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");
            UI.Label(container, orangepanelui, "Orange", 17, UI.TransformToUI4(5f, 195f, 230f, 260f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");

            UI.Label(container, redpanelui, $"{((RedTeam?.members?.Count == null) ? 0 : RedTeam?.members?.Count)}/25", 15, UI.TransformToUI4(5f, 195f, 113f, 143f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");
            UI.Label(container, greenpanelui, $"{((GreenTeam?.members?.Count == null) ? 0 : GreenTeam?.members?.Count)}/25", 15, UI.TransformToUI4(5f, 195f, 113f, 143f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");
            UI.Label(container, bluepanelui, $"{((BlueTeam?.members?.Count == null) ? 0 : BlueTeam?.members?.Count)}/25", 15, UI.TransformToUI4(5f, 195f, 113f, 143f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");
            UI.Label(container, orangepanelui, $"{((OrangeTeam?.members?.Count == null) ? 0 : OrangeTeam?.members?.Count)}/25", 15, UI.TransformToUI4(5f, 195f, 113f, 143f, 200f, 289f), TextAnchor.MiddleCenter, "1 1 1 1");

            CuiHelper.DestroyUi(player, teampanelui);
            CuiHelper.AddUi(player, container);
        }
        void SendScoreboardUI(BasePlayer player)
        {
            if (configdata.showscoreboard == false)
                return;
            CuiElementContainer container = UI.Container(scoreboardui, "0 0 0 0", UI.TransformToUI4(1767f, 1907f, 948f, 1055f));
            UI.Image(container, scoreboardui, GetImage("scoreboard"), UI4.Full);
            UI.Label(container, scoreboardui, "<color=#04dd14>Green Team:</color>", 10, UI.TransformToUI4(12f, 140f, 60f, 79f, 140f, 107f),TextAnchor.MiddleLeft);
            UI.Label(container, scoreboardui, "<color=#ff0000>Red Team:</color>", 10, UI.TransformToUI4(12f, 140f, 43f, 62f, 140f, 107f), TextAnchor.MiddleLeft);
            UI.Label(container, scoreboardui, "<color=#09bcff>Blue Team:</color>", 10, UI.TransformToUI4(12f, 140f, 28f, 47f, 140f, 107f), TextAnchor.MiddleLeft);
            UI.Label(container, scoreboardui, "<color=#ffa500>Orange Team:</color>", 10, UI.TransformToUI4(12f, 140f, 13f, 32f, 140f, 107f), TextAnchor.MiddleLeft);

            UI.Label(container, scoreboardui, $"<color=#04dd14>{groupData.GreenTeam.killCount}</color>", 10, UI.TransformToUI4(102f, 140f, 60f, 79f, 140f, 107f), TextAnchor.MiddleLeft);
            UI.Label(container, scoreboardui, $"<color=#ff0000>{groupData.RedTeam.killCount}</color>", 10, UI.TransformToUI4(102f, 140f, 43f, 62f, 140f, 107f), TextAnchor.MiddleLeft);
            UI.Label(container, scoreboardui, $"<color=#09bcff>{groupData.BlueTeam.killCount}</color>", 10, UI.TransformToUI4(102f, 140f, 28f, 47f, 140f, 107f), TextAnchor.MiddleLeft);
            UI.Label(container, scoreboardui, $"<color=#ffa500>{groupData.OrangeTeam.killCount}</color>", 10, UI.TransformToUI4(102f, 140f, 13f, 32f, 140f, 107f), TextAnchor.MiddleLeft);
            CuiHelper.DestroyUi(player, scoreboardui);
            CuiHelper.AddUi(player, container);
        }
        void RefreshScoreboard()
        {
            foreach (BasePlayer activeplayer in BasePlayer.activePlayerList)
            {
                SendScoreboardUI(activeplayer);
            }
        }

        [ConsoleCommand("teamidentifier.select")]
        void SelectTeam(ConsoleSystem.Arg args)
        {
            BasePlayer player = args.Player();
            string color = args.GetString(0);
            if (player == null || string.IsNullOrEmpty(color))
                return;

            if (player.currentTeam != 0UL)
            {
                PrintToChat(player, "You already have a team");
                return;
            }

            if (color == "red" && !IsTeamFull(TeamType.Red))
                ChangeTeam(player, TeamType.Red);
            else if (color == "blue" && !IsTeamFull(TeamType.Blue))
                ChangeTeam(player, TeamType.Blue);
            else if (color == "green" && !IsTeamFull(TeamType.Green))
                ChangeTeam(player, TeamType.Green);
            else if (color == "orange" && !IsTeamFull(TeamType.Orange))
                ChangeTeam(player, TeamType.Orange);
            else
            {
                player.ChatMessage("Selected team is full");
                CuiHelper.DestroyUi(player, teampanelui);
                SendSelectTeamUI(player);
                return;
            }

            CuiHelper.DestroyUi(player, teampanelui);

            if (color == "red")
                MovePosition(player, TeamType.Red, player.IsSleeping());
            else if (color == "green")
                MovePosition(player, TeamType.Green, player.IsSleeping());
            else if (color == "orange")
                MovePosition(player, TeamType.Orange, player.IsSleeping());
            else if (color == "blue")
                MovePosition(player, TeamType.Blue, player.IsSleeping());

            player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            player.SendNetworkUpdateImmediate(true);
        }
        public static class UI
        {
            public static CuiElementContainer Container(string panelName, string color, UI4 dimensions, bool useCursor = false, string parent = "Overlay")
            {
                CuiElementContainer container = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() },
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return container;
            }


            public static CuiElementContainer Popup(string panelName, string text, int size, UI4 dimensions, TextAnchor align = TextAnchor.MiddleCenter, string parent = "Overlay")
            {
                CuiElementContainer container = UI.Container(panelName, "0 0 0 0", dimensions, false);

                UI.Label(container, panelName, text, size, UI4.Full, align);

                return container;
            }

            public static void Panel(CuiElementContainer container, string panel, string color, UI4 dimensions)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                },
                panel);
            }

            public static void Label(CuiElementContainer container, string panel, string text, int size, UI4 dimensions, TextAnchor align = TextAnchor.MiddleCenter, string color = "1 1 1 1", string font = "RobotoCondensed-Bold.ttf")
            {
                container.Add(new CuiLabel
                {
                    Text = { FontSize = size, Align = align, Text = text, Color = color, Font = font },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }

                },
                panel);
            }

            public static void Button(CuiElementContainer container, string parent, string color, string text, int size, UI4 dimensions, string command,TextAnchor align = TextAnchor.MiddleCenter, string panelname = null)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                parent,panelname);
            }

            public static void Input(CuiElementContainer container, string panel, string text, int size, string command, UI4 dimensions)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = TextAnchor.MiddleLeft,
                            CharsLimit = 300,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text
                        },
                        new CuiRectTransformComponent {AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }

            public static void Image(CuiElementContainer container, string parent, string png, UI4 dimensions)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = parent,
                    Components =
                    {
                        new CuiRawImageComponent {Png = png },
                        new CuiRectTransformComponent { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }
            public static void Image(CuiElementContainer container, string name, string parent, string png, UI4 dimensions)
            {
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = parent,
                    Components =
                    {
                        new CuiRawImageComponent {Png = png },
                        new CuiRectTransformComponent { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }

            public static void Toggle(CuiElementContainer container, string panel, string boxColor, int fontSize, UI4 dimensions, string command, bool isOn)
            {
                UI.Panel(container, panel, boxColor, dimensions);

                if (isOn)
                    UI.Label(container, panel, "✔", fontSize, dimensions);

                UI.Button(container, panel, "0 0 0 0", string.Empty, 0, dimensions, command);
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                    hexColor = hexColor.TrimStart('#');

                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);

                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }

            public static UI4 TransformToUI4(float xmin, float xmax, float ymin, float ymax, float panelwidth = 1920f, float panelheight = 1080f)
            {
                float _xmin = xmin / panelwidth;
                float _xmax = xmax / panelwidth;
                float _ymin = ymin / panelheight;
                float _ymax = ymax / panelheight;
                return new UI4(_xmin, _ymin, _xmax, _ymax);
            }

            public static void CuiElement(CuiElementContainer container, UI4 dimensions, string panel, string cuiElementColor, string sprite, string material)
            {
                container.Add(new CuiElement()
                {
                    Parent = panel,
                    Components =
                    {
                    new CuiRawImageComponent { Color = cuiElementColor, Sprite = sprite, Material = material },
                    new CuiRectTransformComponent{ AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }
        }

        public class UI4
        {
            public float xMin, yMin, xMax, yMax;

            public UI4(float xMin, float yMin, float xMax, float yMax)
            {
                this.xMin = xMin;
                this.yMin = yMin;
                this.xMax = xMax;
                this.yMax = yMax;
            }

            public string GetMin() => $"{xMin} {yMin}";

            public string GetMax() => $"{xMax} {yMax}";

            private static UI4 _full;

            public static UI4 Full
            {
                get
                {
                    if (_full == null)
                        _full = new UI4(0, 0, 1, 1);
                    return _full;
                }
            }
        }
        #endregion
        #region Classes
        public class SpawnPoints
        {
            public Vector3 RedSpawnPoint = Vector3.zero;
            public Vector3 BlueSpawnPoint = Vector3.zero;
            public Vector3 GreenSpawnPoint = Vector3.zero;
            public Vector3 OrangeSpawnPoint = Vector3.zero;
        }
        #endregion
        #region Enums
        public enum TeamType {Green, Red, Blue, Orange}
        #endregion
    }
}