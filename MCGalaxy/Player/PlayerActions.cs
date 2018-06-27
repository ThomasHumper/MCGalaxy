﻿/*
    Copyright 2011 MCForge
        
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.Threading;
using MCGalaxy.Events.PlayerEvents;
using MCGalaxy.Games;
using MCGalaxy.Commands.World;

namespace MCGalaxy {
    public static class PlayerActions {
        
        /// <summary> Moves the player to the specified map. </summary>
        public static bool ChangeMap(Player p, string name) { return ChangeMap(p, null, name); }
        
        /// <summary> Moves the player to the specified map. </summary>
        public static bool ChangeMap(Player p, Level lvl) { return ChangeMap(p, lvl, null); }
        
        static bool ChangeMap(Player p, Level lvl, string name) {
            if (Interlocked.CompareExchange(ref p.UsingGoto, 1, 0) == 1) {
                Player.Message(p, "Cannot use /goto, already joining a map."); return false;
            }
            Level oldLevel = p.level;
            bool didJoin = false;
            
            try {
                didJoin = name == null ? GotoLevel(p, lvl) : GotoMap(p, name);
            } finally {
                Interlocked.Exchange(ref p.UsingGoto, 0);
                Server.DoGC();
            }
            
            if (!didJoin) return false;
            oldLevel.AutoUnload();
            return true;
        }
        
        
        static bool GotoMap(Player p, string name) {
            Level lvl = LevelInfo.FindExact(name);
            if (lvl != null) return GotoLevel(p, lvl);
            
            if (ServerConfig.AutoLoadMaps) {
                string map = Matcher.FindMaps(p, name);
                if (map == null) return false;
                
                lvl = LevelInfo.FindExact(map);
                if (lvl != null) return GotoLevel(p, lvl);
                return LoadOfflineLevel(p, map);
            } else {
                lvl = Matcher.FindLevels(p, name);
                if (lvl == null) {
                    Player.Message(p, "There is no level \"{0}\" loaded. Did you mean..", name);
                    Command.Find("Search").Use(p, "levels " + name);
                    return false;
                }
                return GotoLevel(p, lvl);
            }
        }
        
        static bool LoadOfflineLevel(Player p, string name) {
            string propsPath = LevelInfo.PropsPath(name);
            LevelConfig cfg = new LevelConfig();
            cfg.Load(propsPath);
            
            if (!cfg.LoadOnGoto) {
                Player.Message(p, "Level \"{0}\" cannot be loaded using %T/Goto.", name);
                return false;
            }
            
            LevelAccessController visitAccess = new LevelAccessController(cfg, name, true);
            bool ignorePerms = p.summonedMap != null && p.summonedMap.CaselessEq(name);
            if (!visitAccess.CheckDetailed(p, ignorePerms)) return false;
            
            CmdLoad.LoadLevel(p, name, true);
            Level lvl = LevelInfo.FindExact(name);
            if (lvl != null) return GotoLevel(p, lvl);

            Player.Message(p, "Level \"{0}\" failed to be auto-loaded.", name);
            return false;
        }
        
        static bool GotoLevel(Player p, Level lvl) {
            if (p.level == lvl) { Player.Message(p, "You are already in {0}%S.", lvl.ColoredName); return false; }
            if (!lvl.CanJoin(p)) return false;

            p.Loading = true;
            Entities.DespawnEntities(p);
            Level oldLevel = p.level;
            p.level = lvl;
            p.SendMap(oldLevel);
            
            PostSentMap(p, oldLevel, lvl, true);
            return true;
        }
        
        internal static void PostSentMap(Player p, Level prevLevel, Level level, bool announce) {
            Position pos = level.SpawnPos;
            Orientation rot = p.Rot;
            byte yaw = level.rotx, pitch = level.roty;
            
            OnPlayerSpawningEvent.Call(p, ref pos, ref yaw, ref pitch, false);
            rot.RotY = yaw; rot.HeadX = pitch;
            p.Pos = pos;
            p.SetYawPitch(yaw, pitch);
            
            Entities.SpawnEntities(p, pos, rot);
            CheckGamesJoin(p, prevLevel);
            OnJoinedLevelEvent.Call(p, prevLevel, level, ref announce);
            if (!announce || !ServerConfig.ShowWorldChanges) return; 
            
            announce = !p.hidden && ServerConfig.IRCShowWorldChanges;
            string msg = p.level.IsMuseum ? "λNICK %Swent to the " : "λNICK %Swent to ";
            Chat.MessageFrom(ChatScope.Global, p, msg + level.ColoredName,
                             null, FilterGoto(p), announce);
        }
        
        static ChatMessageFilter FilterGoto(Player source) {
            return (pl, obj) => Entities.CanSee(pl, source) && !pl.Ignores.WorldChanges;
        }
        
        static void CheckGamesJoin(Player p, Level oldLvl) {
            if (p.inTNTwarsMap) p.canBuild = true;
            TntWarsGame game = TntWarsGame.Find(p.level);
            if (game == null) return;
            
            if (game.GameStatus != TntWarsGame.TntWarsStatus.Finished &&
                game.GameStatus != TntWarsGame.TntWarsStatus.WaitingForPlayers) {
                p.canBuild = false;
                Player.Message(p, "TNT Wars: Disabled your building because you are in a TNT Wars map!");
            }
            p.inTNTwarsMap = true;
        }
    }
}
