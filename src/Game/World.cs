#region license
// Copyright (C) 2020 ClassicUO Development Community on Github
// 
// This project is an alternative client for the game Ultima Online.
// The goal of this is to develop a lightweight client considering
// new technologies.
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
// 
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.
#endregion

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Map;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Utility.Platforms;

using Microsoft.Xna.Framework;

namespace ClassicUO.Game
{
    enum SCAN_TYPE_OBJECT
    {
        STO_HOSTILE = 0,
        STO_PARTY,
        STO_FOLLOWERS,
        STO_OBJECTS,
        STO_MOBILES
    }
    enum SCAN_MODE_OBJECT
    {
        SMO_NEXT = 0,
        SMO_PREV,
        SMO_NEAREST
    }

    internal static class World
    {
        private static readonly EffectManager _effectManager = new EffectManager();
        private static readonly List<uint> _toRemove = new List<uint>();

        public static Point RangeSize;

        public static ObjectPropertiesListManager OPL { get; } = new ObjectPropertiesListManager();

        public static CorpseManager CorpseManager { get; } = new CorpseManager();

        public static PartyManager Party { get; } = new PartyManager();

        public static HouseManager HouseManager { get; } = new HouseManager();

        public static EntityCollection<Entity> Objects { get; } = new EntityCollection<Entity>();

        public static PlayerMobile Player;

        public static Map.Map Map { get; private set; }

        public static byte ClientViewRange { get; set; } = Constants.MAX_VIEW_RANGE;

        public static bool SkillsRequested { get; set; }

        public static Seasons Season { get; private set; } = Seasons.Summer;
        public static Seasons OldSeason { get; set; } = Seasons.Summer;

        public static int OldMusicIndex { get; set; }

        public static WorldTextManager WorldTextManager { get; } = new WorldTextManager();

        public static JournalManager Journal { get; } = new JournalManager();

        public static HouseCustomizationManager CustomHouseManager;

        public static WorldMapEntityManager WMapManager = new WorldMapEntityManager();

        public static ActiveSpellIconsManager ActiveSpellIcons = new ActiveSpellIconsManager();

        public static uint LastObject, ObjectToRemove;


        public static int MapIndex
        {
            get => Map?.Index ?? -1;
            set
            {
                if (MapIndex != value)
                {
                    InternalMapChangeClear(true);

                    if (value < 0 && Map != null)
                    {
                        Map.Destroy();
                        Map = null;

                        return;
                    }

                    if (Map != null)
                    {
                        if (MapIndex >= 0)
                            Map.Destroy();

                        ushort x = Player.X;
                        ushort y = Player.Y;
                        sbyte z = Player.Z;

                        Map = null;

                        if (value >= Constants.MAPS_COUNT)
                            value = 0;

                        Map = new Map.Map(value)
                        {
                            Center = new Point(x, y)
                        };
                        Map.Initialize();

                        Player.X = x;
                        Player.Y = y;
                        Player.Z = z;
                        Player.UpdateScreenPosition();
                        Player.AddToTile();

                        Player.ClearSteps();
                    }
                    else
                    {
                        Map = new Map.Map(value);

                        if (Player != null)
                            Map.Center = new Point(Player.X, Player.Y);
                        Map.Initialize();
                    }

                    UoAssist.SignalMapChanged(value);
                }
            }
        }

        public static bool InGame => Player != null && Map != null;

        public static IsometricLight Light { get; } = new IsometricLight
        {
            Overall = 0,
            Personal = 0,
            RealOverall = 0,
            RealPersonal = 0
        };

        public static LockedFeatures ClientLockedFeatures { get; } = new LockedFeatures();

        public static ClientFeatures ClientFeatures { get; } = new ClientFeatures();

        public static string ServerName { get; set; }


        public static void ChangeSeason(Seasons season, int music)
        {
            Season = season;

            foreach (int i in Map.GetUsedChunks())
            {
                Chunk chunk = Map.Chunks[i];

                for (int x = 0; x < 8; x++)
                {
                    for (int y = 0; y < 8; y++)
                    {
                        for (GameObject obj = chunk.GetHeadObject(x, y); obj != null; obj = obj.TNext)
                        {
                            obj.UpdateGraphicBySeason();
                        }
                    }
                }
            }

            Client.Game.Scene.Audio.PlayMusic(music, true);
        }


        public static void Update(double totalMS, double frameMS)
        {
            if (Player != null)
            {
                if (SerialHelper.IsValid(ObjectToRemove))
                {
                    Item rem = (Item) Get(ObjectToRemove);
                    ObjectToRemove = 0;

                    if (rem != null)
                    {
                        RemoveObject(rem, true);

                        if (rem.Layer == Layer.OneHanded || rem.Layer == Layer.TwoHanded)
                            Player.UpdateAbilities();

                        if (SerialHelper.IsMobile(rem.Container))
                        {
                            UIManager.GetGump<PaperDollGump>(rem.Container)?.RequestUpdateContents();
                        }
                        else if (SerialHelper.IsItem(rem.Container))
                        {
                            UIManager.GetGump<ContainerGump>(rem.Container)?.RequestUpdateContents();
                        }
                    }
                }

                foreach (Entity entity in Objects)
                {
                    entity.Update(totalMS, frameMS);

                    bool remove = entity.Distance > ClientViewRange;

                    if (entity is Item item)
                    {
                        if (item.OnGround && remove)
                        {
                            if (item.IsMulti)
                            {
                                if (HouseManager.TryToRemove(entity.Serial, ClientViewRange))
                                    RemoveObject(entity.Serial);
                            }
                            else
                                RemoveObject(entity.Serial);
                        }
                        else
                            remove = false;
                    }
                    else 
                    {
                        if (remove)
                        {
                            RemoveObject(entity.Serial);
                        }
                        else
                        {
                            var mob = (Mobile) entity;

                            if (mob.NotorietyFlag == NotorietyFlag.Ally)
                            {
                                WMapManager.AddOrUpdate(
                                                        mob.Serial,
                                                        mob.X,
                                                        mob.Y,
                                                        Utility.MathHelper.PercetangeOf(mob.Hits, mob.HitsMax),
                                                        MapIndex,
                                                        true,
                                                        mob.Name);
                            }
                            else if (Party.Leader != 0 && Party.Contains(mob))
                            {
                                WMapManager.AddOrUpdate(
                                                        mob.Serial,
                                                        mob.X,
                                                        mob.Y,
                                                        Utility.MathHelper.PercetangeOf(mob.Hits, mob.HitsMax),
                                                        MapIndex,
                                                        false,
                                                        mob.Name);
                            }
                        }
                    }

                    if (remove)
                    {
                        _toRemove.Add(entity.Serial);
                    }
                }


                if (_toRemove.Count != 0)
                {
                    for (int i = 0; i < _toRemove.Count; i++)
                        Objects.Remove(_toRemove[i]);

                    _toRemove.Clear();
                }

                
                _effectManager.Update(totalMS, frameMS);
                WorldTextManager.Update(totalMS, frameMS);
                WMapManager.RemoveUnupdatedWEntity();
            }
        }

        public static bool Contains(uint serial)
        {
            return Objects.Contains(serial);
        }

        public static Entity Get(uint serial)
        {
            Entity ent = Objects.Get(serial);

            if (ent != null && ent.IsDestroyed)
                ent = null;

            return ent;
        }

        public static T Get<T>(uint serial) where T : Entity
        {
            T ent = Objects.Get(serial) as T;

            if (ent != null && ent.IsDestroyed)
                ent = null;

            return ent;
        }

        public static Item GetOrCreateItem(uint serial)
        {
            Item item = (Item) Objects.Get(serial);

            if (item != null && item.IsDestroyed)
            {
                Objects.Remove(serial);
                item = null;
            }

            if (item == null /*|| item.IsDestroyed*/)
            {
                item = Item.Create(serial);
                Objects.Add(item);
            }

            return item;
        }

        public static Mobile GetOrCreateMobile(uint serial)
        {
            Mobile mob = (Mobile) Get(serial);

            if (mob != null && mob.IsDestroyed)
            {
                Objects.Remove(serial);
                mob = null;
            }

            if (mob == null)
            {
                mob = Mobile.Create(serial);
                Objects.Add(mob);
            }

            return mob;
        }

        public static bool RemoveObject(uint serial, bool force_remotion = false)
        {
            var entity = Get(serial);

            if (entity == null)
                return false;

            if (entity is Item it && SerialHelper.IsValid(it.Container))
            {
                Get(it.Container)?.Remove(it);
            }

            var first = entity.Items;

            while (first != null)
            {
                var next = first.Next;

                RemoveObject(first as Entity, force_remotion);

                first = next;
            }

            entity.Clear();
            entity.Destroy();

            if (force_remotion)
                Objects.Remove(serial);

            return true;
        }

        internal static void AddEffect(GameEffect effect)
        {
            _effectManager.Add(effect);
        }

        internal static void RemoveEffect(GameEffect effect)
        {
            _effectManager.RemoveEffect(effect);
        }

        public static void AddEffect(GraphicEffectType type, uint source, uint target,
                                     ushort graphic, ushort hue,
                                     ushort srcX, ushort srcY, sbyte srcZ,
                                     ushort targetX, ushort targetY, sbyte targetZ,
                                     byte speed, int duration, bool fixedDir, bool doesExplode, bool hasparticles, GraphicEffectBlendMode blendmode)
        {
            _effectManager.Add(type, source, target, graphic, hue, srcX, srcY, srcZ, targetX, targetY, targetZ, speed, duration, fixedDir, doesExplode, hasparticles, blendmode);
        }

        public static uint SearchObject(uint serial, SCAN_TYPE_OBJECT scanType, SCAN_MODE_OBJECT scanMode)
        {
            Entity first = null, selected = null;
            int distance = int.MaxValue;
            bool currentTargetFound = false;

            if (scanType == SCAN_TYPE_OBJECT.STO_OBJECTS)
            {
                if (scanMode == SCAN_MODE_OBJECT.SMO_NEAREST)
                {
                    foreach (Entity entity in Objects)
                    {
                        if (entity is Item item)
                        {
                            if (item.IsMulti || item.IsDestroyed || !item.OnGround)
                                continue;

                            var dist = item.Distance;

                            if (dist < distance)
                                distance = dist;
                        }
                    }
                }

                foreach (Entity entity in Objects)
                {
                    if (entity is Item item)
                    {
                        if (item.IsMulti || item.IsDestroyed || !item.OnGround)
                            continue;

                        if (!SerialHelper.IsValid(serial))
                            return item;

                        if (scanMode == SCAN_MODE_OBJECT.SMO_NEXT)
                        {
                            if (serial == item)
                            {
                                currentTargetFound = true;
                                continue;
                            }

                            if (first == null)
                                first = item;

                            if (currentTargetFound)
                            {
                                selected = item;
                                break;
                            }
                        }
                        else if (scanMode == SCAN_MODE_OBJECT.SMO_PREV)
                        {
                            if (!currentTargetFound && first != null)
                                selected = first;

                            if (serial == item)
                            {
                                currentTargetFound = true;
                                continue;
                            }

                            first = item;
                        }
                        else if (scanMode == SCAN_MODE_OBJECT.SMO_NEAREST)
                        {
                            if (item.Distance > distance)
                                continue;

                            if (serial == item.Serial)
                            {
                                currentTargetFound = true;
                                continue;
                            }

                            if (first == null)
                                first = item;

                            if (currentTargetFound)
                            {
                                selected = item;
                                break;
                            }
                        }
                        else
                            break;
                    }
                }
            }
            else
            {
                if (scanMode == SCAN_MODE_OBJECT.SMO_NEAREST)
                {
                    foreach (Entity entity in Objects)
                    {
                        if (entity is Mobile mobile)
                        {
                            if (mobile.IsDestroyed || mobile == Player)
                                continue;

                            if (scanType == SCAN_TYPE_OBJECT.STO_PARTY)
                            {
                                if (!Party.Contains(mobile))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_FOLLOWERS)
                            {
                                if (!(mobile.IsRenamable &&
                                      mobile.NotorietyFlag != NotorietyFlag.Invulnerable &&
                                      mobile.NotorietyFlag != NotorietyFlag.Enemy))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_HOSTILE)
                            {
                                if (mobile.NotorietyFlag == NotorietyFlag.Ally ||
                                    mobile.NotorietyFlag == NotorietyFlag.Innocent ||
                                    mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
                                {
                                    continue;
                                }
                            }

                            var dist = mobile.Distance;

                            if (dist < distance)
                                distance = dist;
                        }
                    }
                }

                foreach (Entity entity in Objects)
                {
                    if (entity is Mobile mobile)
                    {
                        if (mobile.IsDestroyed || mobile == Player)
                            continue;

                        if (scanMode == SCAN_MODE_OBJECT.SMO_NEXT)
                        {
                            if (scanType == SCAN_TYPE_OBJECT.STO_PARTY)
                            {
                                if (!Party.Contains(mobile))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_FOLLOWERS)
                            {
                                if (!(mobile.IsRenamable &&
                                    mobile.NotorietyFlag != NotorietyFlag.Invulnerable &&
                                    mobile.NotorietyFlag != NotorietyFlag.Enemy))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_HOSTILE)
                            {
                                if (mobile.NotorietyFlag == NotorietyFlag.Ally ||
                                    mobile.NotorietyFlag == NotorietyFlag.Innocent ||
                                    mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
                                {
                                    continue;
                                }
                            }

                            if (serial == mobile)
                            {
                                currentTargetFound = true;
                                continue;
                            }

                            if (first == null)
                                first = mobile;

                            if (currentTargetFound)
                            {
                                selected = mobile;
                                break;
                            }
                        }
                        else if (scanMode == SCAN_MODE_OBJECT.SMO_PREV)
                        {
                            if (scanType == SCAN_TYPE_OBJECT.STO_PARTY)
                            {
                                if (!Party.Contains(mobile))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_FOLLOWERS)
                            {
                                if (!(mobile.IsRenamable &&
                                      mobile.NotorietyFlag != NotorietyFlag.Invulnerable &&
                                      mobile.NotorietyFlag != NotorietyFlag.Enemy))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_HOSTILE)
                            {
                                if (mobile.NotorietyFlag == NotorietyFlag.Ally ||
                                    mobile.NotorietyFlag == NotorietyFlag.Innocent ||
                                    mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
                                {
                                    continue;
                                }
                            }

                            if (!currentTargetFound && first != null)
                                selected = first;

                            if (serial == mobile)
                            {
                                currentTargetFound = true;
                                continue;
                            }

                            first = mobile;
                        }
                        else if (scanMode == SCAN_MODE_OBJECT.SMO_NEAREST)
                        {
                            if (scanType == SCAN_TYPE_OBJECT.STO_PARTY)
                            {
                                if (!Party.Contains(mobile))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_FOLLOWERS)
                            {
                                if (!(mobile.IsRenamable &&
                                      mobile.NotorietyFlag != NotorietyFlag.Invulnerable &&
                                      mobile.NotorietyFlag != NotorietyFlag.Enemy))
                                {
                                    continue;
                                }
                            }
                            else if (scanType == SCAN_TYPE_OBJECT.STO_HOSTILE)
                            {
                                if (mobile.NotorietyFlag == NotorietyFlag.Ally ||
                                    mobile.NotorietyFlag == NotorietyFlag.Innocent ||
                                    mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
                                {
                                    continue;
                                }
                            }

                            if (mobile.Distance > distance)
                                continue;

                            if (serial == mobile.Serial)
                            {
                                currentTargetFound = true;
                                continue;
                            }

                            if (first == null)
                                first = mobile;

                            if (currentTargetFound)
                            {
                                selected = mobile;
                                break;
                            }
                        }
                        else
                            break;
                    }
                }
            }

            if (first != null && selected == null)
            {
                return first;
            }

            return selected?.Serial ?? 0;
        }

        public static void Clear()
        {
            foreach (var entity in Objects)
            {
                RemoveObject(entity);
            }


            ObjectToRemove = 0;
            LastObject = 0;
            HouseManager?.Clear();
            Objects.Clear();
            Player?.Destroy();
            Player = null;
            Map?.Destroy();
            Map = null;
            Light.Overall = Light.RealOverall = 0;
            Light.Personal = Light.RealPersonal = 0;
            ClientFeatures.SetFlags(0);
            ClientLockedFeatures.SetFlags(0);
            HouseManager?.Clear();
            Party?.Clear();
            TargetManager.LastAttack = 0;
            MessageManager.PromptData = default;
            _effectManager.Clear();
            _toRemove.Clear();
            CorpseManager.Clear();
            OPL.Clear();
            WMapManager.Clear();

            Season = Seasons.Summer;
            OldSeason = Seasons.Summer;

            Journal.Clear();
            WorldTextManager.Clear();
            ActiveSpellIcons.Clear();

            SkillsRequested = false;
        }

        private static void InternalMapChangeClear(bool noplayer)
        {
            if (!noplayer)
            {
                Map.Destroy();
                Map = null;
                Player.Destroy();
                Player = null;
            }

            foreach (var entity in Objects)
            {
                if (noplayer && Player != null && !Player.IsDestroyed)
                {
                    if (entity == Player || (entity is Item item && item.RootContainer == Player))
                        continue;
                }

                _toRemove.Add(entity.Serial);
            }

            
            foreach (var serial in _toRemove)
            {
                RemoveObject(serial, true);
            }

            _toRemove.Clear();
        }
    }
}