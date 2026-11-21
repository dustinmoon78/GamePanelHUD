using System;
using System.Collections.Generic;
using EFT.Quests;
using GamePanelHUDCompass.Models;
using UnityEngine;

namespace GamePanelHUDCompass.VisitApi
{
    /// <summary>
    /// 把 VisitAPI 区域表翻译成 Compass 的 StaticModel。
    ///
    /// 与 VisitAPI 自己的做法一致：区域本身不带任务归属，归属来自任务条件里的 zoneId。
    /// 因此这里反向扫一遍「进行中任务」的条件，把 zoneId 与区域表的 id 对上，
    /// 顺带拿到任务的 TraderId —— Compass 的静态标记就是靠它绑商人头像的。
    /// </summary>
    internal static class VisitZoneStaticBuilder
    {
        /// <summary>
        /// 收集当前战局应当显示的剧情区域标记。
        /// </summary>
        /// <param name="zones">VisitAPI 区域表</param>
        /// <param name="locationId">当前地图 id（GameWorld.LocationId）</param>
        /// <param name="gameTime">游戏内时间，用于时段过滤；null 表示不做时段过滤</param>
        /// <param name="quests">玩家任务簿（QuestBook 即 BindableList&lt;Quest&gt;）</param>
        /// <param name="conditionsOf">取任务全部条件的回调（含 counterCreator 展开）</param>
        /// <summary>本次 Build 的诊断数据（只用于排查，不参与逻辑）</summary>
        internal static string LastDiag = "";

        /// <summary>本次 Build 里出现过的 zoneId 样本（最多 8 个）</summary>
        internal static readonly List<string> SampledZoneIds = new List<string>();

        private static void Sample(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId) || SampledZoneIds.Contains(zoneId))
                return;

            if (SampledZoneIds.Count < 8)
                SampledZoneIds.Add(zoneId);
        }

        public static List<StaticModel> Build(
            List<VisitZone> zones,
            string locationId,
            DateTime? gameTime,
            IEnumerable<Quest> quests,
            Func<Quest, IEnumerable<object>> conditionsOf)
        {
            var result = new List<StaticModel>();

            SampledZoneIds.Clear();

            if (zones == null || zones.Count == 0 || string.IsNullOrEmpty(locationId) || quests == null)
            {
                LastDiag = string.Format("zones={0} loc={1} quests={2}（空输入早退）",
                    zones?.Count ?? -1, locationId ?? "null", quests == null ? "null" : "有");

                return result;
            }

            var zonesById = new Dictionary<string, List<VisitZone>>(StringComparer.Ordinal);

            foreach (var zone in zones)
            {
                if (string.IsNullOrEmpty(zone.Id) || !zone.CoversLocation(locationId))
                    continue;

                if (!zonesById.TryGetValue(zone.Id, out var list))
                {
                    list = new List<VisitZone>(1);
                    zonesById.Add(zone.Id, list);
                }

                list.Add(zone);
            }

            if (zonesById.Count == 0)
            {
                LastDiag = string.Format("地图={0} 区域表={1} 本图区域=0（没有区域覆盖这张图）",
                    locationId, zones.Count);

                return result;
            }

            var questTotal = 0;
            var questStarted = 0;
            var condTotal = 0;
            var condExpanded = 0;
            var zoneHit = 0;

            foreach (var quest in quests)
            {
                if (quest == null)
                    continue;

                questTotal++;

                // 不只 Started：可交付（AvailableForFinish）与可接取（AvailableForStart）的地点
                // 同样该标出来——剧情任务的目标点往往在「可交付」时还需要玩家跑一趟。
                var status = quest.QuestStatus;

                if (status != EQuestStatus.Started &&
                    status != EQuestStatus.AvailableForFinish &&
                    status != EQuestStatus.AvailableForStart)
                    continue;

                questStarted++;

                var template = quest.Template;

                if (template == null)
                    continue;

                var questName = template.Name;
                var nameKey = string.IsNullOrEmpty(questName) ? template.Id + " name" : questName;
                var traderId = template.TraderId;

                IEnumerable<object> conditions;

                try
                {
                    conditions = conditionsOf(quest);
                }
                catch (Exception)
                {
                    continue;
                }

                if (conditions == null)
                    continue;

                foreach (var condition in conditions)
                {
                    condTotal++;

                    if (!(condition is ConditionCounterCreator))
                        condExpanded++;

                    var before = result.Count;

                    Collect(condition, zonesById, nameKey, traderId, gameTime, result);

                    zoneHit += result.Count - before;
                }
            }

            LastDiag = string.Format(
                "地图={0} 区域表={1} 本图区域={2} 任务={3} 选中={4} 条件={5}(非counter={6}) 命中={7} 产出={8} | 条件zoneId=[{9}] | 本图zoneId=[{10}]",
                locationId, zones.Count, zonesById.Count, questTotal, questStarted,
                condTotal, condExpanded, zoneHit, result.Count,
                string.Join(",", SampledZoneIds.ToArray()),
                string.Join(",", new List<string>(zonesById.Keys).ToArray()));

            return result;
        }

        private static void Collect(
            object condition,
            Dictionary<string, List<VisitZone>> zonesById,
            string nameKey,
            string traderId,
            DateTime? gameTime,
            List<StaticModel> result)
        {
            switch (condition)
            {
                case ConditionLeaveItemAtLocation leave:
                    Sample(leave.zoneId);
                    Emit(leave.zoneId, leave.id, !leave.IsNecessary, nameKey, traderId, gameTime, zonesById, result);
                    break;

                case ConditionPlaceBeacon beacon:
                    Sample(beacon.zoneId);
                    Emit(beacon.zoneId, beacon.id, !beacon.IsNecessary, nameKey, traderId, gameTime, zonesById, result);
                    break;

                case ConditionVisitPlace visit:
                    Sample(visit.target);
                    Emit(visit.target, visit.id, false, nameKey, traderId, gameTime, zonesById, result);
                    break;

                case ConditionInZone inZone:
                    if (inZone.zoneIds == null)
                        break;

                    foreach (var zoneId in inZone.zoneIds)
                    {
                        Sample(zoneId);
                        Emit(zoneId, inZone.id, false, nameKey, traderId, gameTime, zonesById, result);
                    }

                    break;
            }
        }

        private static void Emit(
            string zoneId,
            string conditionId,
            bool isOptional,
            string nameKey,
            string traderId,
            DateTime? gameTime,
            Dictionary<string, List<VisitZone>> zonesById,
            List<StaticModel> result)
        {
            if (string.IsNullOrEmpty(zoneId))
                return;

            if (!zonesById.TryGetValue(zoneId, out var zones))
                return;

            foreach (var zone in zones)
            {
                if (!zone.HoursOk(gameTime))
                    continue;

                result.Add(new StaticModel
                {
                    Id = "visit:" + zoneId + ":" + conditionId,
                    Where = zone.InteractAt ?? zone.Position,
                    ZoneId = zoneId,
                    NameKey = nameKey,
                    DescriptionKey = conditionId,
                    TraderId = traderId,
                    IsNotNecessary = isOptional,
                    InfoType = StaticModel.Type.VisitZone,
                    NameText = null,
                    DescriptionText = BuildDescriptionText(zone)
                });
            }
        }

        /// <summary>
        /// 描述行：优先用 VisitAPI 给的交互提示（例如对讲机的「使用」），
        /// 没有则标出区域类型，便于和原生任务条件区分。
        /// </summary>
        private static string BuildDescriptionText(VisitZone zone)
        {
            if (!string.IsNullOrEmpty(zone.InteractPrompt))
                return zone.InteractPrompt;

            return zone.IsPlaceItem ? "放置区域" : "剧情区域";
        }
    }
}
