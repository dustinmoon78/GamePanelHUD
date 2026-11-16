using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Interactive;
using UnityEngine;
#if !UNITY_EDITOR
using EFT.Quests;
using KmyTarkovUtils;
using static KmyTarkovApi.EFTHelpers;
using GamePanelHUDCore.Models;
using GamePanelHUDCompass.Models;
using GamePanelHUDCompass.VisitApi;
using SettingsModel = GamePanelHUDCompass.Models.SettingsModel;

#endif

namespace GamePanelHUDCompass.Controllers
{
    public class CompassStaticHUDController : MonoBehaviour
#if !UNITY_EDITOR
        , KmyTarkovUtils.IUpdate
#endif
    {
#if !UNITY_EDITOR

        private void Start()
        {
            HUDCoreModel.Instance.WorldStart += OnWorldStart;

            HUDCoreModel.Instance.UpdateManger.Register(this);
        }

        public void CustomUpdate()
        {
            var hudCoreModel = HUDCoreModel.Instance;
            var compassHUDModel = CompassHUDModel.Instance;
            var compassStaticHUDModel = CompassStaticHUDModel.Instance;
            var settingsModel = SettingsModel.Instance;

            compassStaticHUDModel.CompassStaticHUDSw =
                compassHUDModel.CompassHUDSw && settingsModel.KeyCompassStaticHUDSw.Value;

            if (hudCoreModel.HasPlayer)
            {
                compassStaticHUDModel.CompassStatic.TriggerZones = hudCoreModel.YourPlayer.TriggerZones;

                //Performance Optimization
                if (Time.frameCount % 20 == 0)
                {
                    var hashSet = _InventoryHelper.EquipmentItemHashSet;

                    hashSet.UnionWith(_InventoryHelper.QuestRaidItemHashSet);

                    compassStaticHUDModel.CompassStatic.EquipmentAndQuestRaidItemHashSet = hashSet;
                }

                RefreshVisitZones(compassStaticHUDModel);
            }
            else
            {
                compassStaticHUDModel.CompassStatic.EquipmentAndQuestRaidItemHashSet = null;
                compassStaticHUDModel.AirdropCount = 0;
            }
        }

        /// <summary>区域表重建的最小间隔（秒）——任务状态变化不会有事件通知，只能低频轮询</summary>
        private const float VisitZoneRefreshInterval = 5f;

        /// <summary>
        /// VisitAPI 剧情区域的拉取与重建。
        ///
        /// 流程：进战局时发一次请求（幂等）→ 后台拉回来后接走 →
        /// 每 5 秒按「进行中任务的条件 zoneId」重建一次标记集合，多退少补。
        /// VisitAPI 未安装时客户端拿不到区域表，整段静默跳过，不影响原生标记。
        /// </summary>
        private static void RefreshVisitZones(CompassStaticHUDModel compassStaticHUDModel)
        {
            if (!SettingsModel.Instance.KeyVisitZone.Value)
                return;

            // 首次进战局才发请求；失败也算请求过，不再重发
            VisitZoneClient.Request();

            if (VisitZoneClient.TryTake(out var zones))
            {
                compassStaticHUDModel.VisitZonesDirty = true;
            }

            // 区域表还没到手（首次拉取中或未装 VisitAPI）时不要清 dirty，等表到了再建
            if (VisitZoneClient.Zones == null)
                return;

            if (!compassStaticHUDModel.VisitZonesDirty)
                return;

            if (Time.time - compassStaticHUDModel.VisitZonesLastRefresh < VisitZoneRefreshInterval)
                return;

            compassStaticHUDModel.VisitZonesLastRefresh = Time.time;
            compassStaticHUDModel.VisitZonesDirty = false;

            var hudCoreModel = HUDCoreModel.Instance;

            if (hudCoreModel.YourPlayer == null || hudCoreModel.TheWorld == null)
                return;

            var models = VisitZoneStaticBuilder.Build(
                VisitZoneClient.Zones,
                hudCoreModel.TheWorld.LocationId,
                hudCoreModel.TheWorld.GameDateTime?.Calculate(),
                _AbstractQuestControllerClassHelper.Quests,
                QuestConditionsOf);

            var wanted = new HashSet<string>();

            foreach (var model in models)
            {
                wanted.Add(model.Id);

                if (compassStaticHUDModel.VisitZoneShownIds.Contains(model.Id))
                    continue;

                compassStaticHUDModel.VisitZoneShownIds.Add(model.Id);
                compassStaticHUDModel.ShowStatic?.Invoke(model);
            }

            // 任务完成 / 离开区域的标记要撤掉，否则会一直挂在罗盘上
            if (compassStaticHUDModel.VisitZoneShownIds.Count == 0)
                return;

            var stale = new List<string>();

            foreach (var id in compassStaticHUDModel.VisitZoneShownIds)
            {
                if (!wanted.Contains(id))
                    stale.Add(id);
            }

            foreach (var id in stale)
            {
                compassStaticHUDModel.VisitZoneShownIds.Remove(id);
                compassStaticHUDModel.DestroyStatic?.Invoke(id);
            }
        }

        /// <summary>
        /// 展开任务的全部条件——原生 Compass 只处理 NecessaryConditions 顶层的四类条件，
        /// 这里把 counterCreator 内的 VisitPlace / InZone 也解出来，才能对上 VisitAPI 的 zoneId。
        /// </summary>
        private static IEnumerable<object> QuestConditionsOf(Quest quest)
        {
            var result = new List<object>();

            foreach (var condition in quest.NecessaryConditions)
            {
                result.Add(condition);

                if (!(condition is ConditionCounterCreator counterCreator))
                    continue;

                var templateConditions = _ConditionCounterCreatorHelper.RefTemplateConditions
                    .GetValue(counterCreator);

                var conditions = _ConditionCounterTemplateHelper.RefConditions.GetValue(templateConditions);

                foreach (var inner in conditions)
                {
                    result.Add(inner);
                }
            }

            return result;
        }

        private static void OnWorldStart(GameWorld __instance)
        {
            var hudCoreModel = HUDCoreModel.Instance;
            var compassStaticHUDModel = CompassStaticHUDModel.Instance;

            if (hudCoreModel.YourPlayer is HideoutPlayer)
                return;

            ShowQuest(hudCoreModel.YourPlayer.Profile, hudCoreModel.TheGame, compassStaticHUDModel.ShowStatic);
            ShowExfiltration(hudCoreModel.YourPlayer.Profile, hudCoreModel.YourPlayer.ProfileId,
                compassStaticHUDModel.ShowStatic);

            // VisitAPI 剧情区域：服务端聚合接口是异步的，这里只发请求，结果在 CustomUpdate 里收。
            // 标记 id 集合在这里清空——上一局的标记已随 WorldDispose 整批销毁，
            // 不清的话新一局会误判「已经创建过」而不再重建。
            compassStaticHUDModel.VisitZoneShownIds.Clear();
            compassStaticHUDModel.VisitZonesLastRefresh = 0f;
            compassStaticHUDModel.VisitZonesDirty = true;

            VisitZoneClient.Request();
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private static void ShowQuest(Profile profile, AbstractGame game, Action<StaticModel> showStatic)
        {
            var hudCoreModel = HUDCoreModel.Instance;

            var questItemDictionary = new Dictionary<string, List<LootItem>>();
            foreach (var lootItem in hudCoreModel.TheWorld.LootList.OfType<LootItem>())
            {
                if (!lootItem.Item.QuestItem)
                    continue;

                if (questItemDictionary.TryGetValue(lootItem.TemplateId, out var lootItemList))
                {
                    lootItemList.Add(lootItem);
                }
                else
                {
                    questItemDictionary.Add(lootItem.TemplateId, new List<LootItem> { lootItem });
                }
            }

            foreach (var quest in _AbstractQuestControllerClassHelper.Quests)
            {
                if (quest.QuestStatus != EQuestStatus.Started)
                    continue;

                var template = quest.Template;

                var locationId = template.LocationId;

                if (locationId != game.LocationObjectId && locationId != "any")
                    continue;

                if ((profile.Side == EPlayerSide.Savage ? EPlayerGroup.Scav : EPlayerGroup.Pmc) !=
                    template.PlayerGroup)
                    continue;

                var name = template.Name;

                var nameKey = string.IsNullOrEmpty(name) ? $"{template.Id} name" : name;

                var traderId = template.TraderId;

                foreach (var condition in quest.NecessaryConditions)
                {
                    var id = condition.id;

                    switch (condition)
                    {
                        case ConditionLeaveItemAtLocation location:
                        {
                            var zoneId = location.zoneId;

                            if (_ZoneHelper.TryGetValues(zoneId,
                                    out IEnumerable<PlaceItemTrigger> triggers))
                            {
                                foreach (var trigger in triggers)
                                {
                                    var staticModel = new StaticModel
                                    {
                                        Id = id,
                                        Where = trigger.transform.position,
                                        ZoneId = zoneId,
                                        Target = location.target,
                                        NameKey = nameKey,
                                        DescriptionKey = id,
                                        TraderId = traderId,
                                        IsNotNecessary = !location.IsNecessary,
                                        InfoType = StaticModel.Type.ConditionLeaveItemAtLocation
                                    };

                                    showStatic(staticModel);
                                }
                            }

                            break;
                        }
                        case ConditionPlaceBeacon beacon:
                        {
                            var zoneId = beacon.zoneId;

                            if (_ZoneHelper.TryGetValues(zoneId,
                                    out IEnumerable<PlaceItemTrigger> triggers))
                            {
                                foreach (var trigger in triggers)
                                {
                                    var staticModel = new StaticModel
                                    {
                                        Id = id,
                                        Where = trigger.transform.position,
                                        ZoneId = zoneId,
                                        Target = beacon.target,
                                        NameKey = nameKey,
                                        DescriptionKey = id,
                                        TraderId = traderId,
                                        IsNotNecessary = !beacon.IsNecessary,
                                        InfoType = StaticModel.Type.ConditionPlaceBeacon
                                    };

                                    showStatic(staticModel);
                                }
                            }

                            break;
                        }
                        case ConditionFindItem findItem:
                        {
                            var itemIds = findItem.target;

                            foreach (var itemId in itemIds)
                            {
                                if (!questItemDictionary.TryGetValue(itemId, out var questItemList))
                                    continue;

                                foreach (var questItem in questItemList)
                                {
                                    var staticModel = new StaticModel
                                    {
                                        Id = id,
                                        Where = questItem.transform.position,
                                        Target = new[] { itemId },
                                        NameKey = nameKey,
                                        DescriptionKey = id,
                                        TraderId = traderId,
                                        IsNotNecessary = !findItem.IsNecessary,
                                        InfoType = StaticModel.Type.ConditionFindItem
                                    };

                                    showStatic(staticModel);
                                }
                            }

                            break;
                        }
                        case ConditionCounterCreator counterCreator:
                        {
                            var templateConditions =
                                _ConditionCounterCreatorHelper.RefTemplateConditions.GetValue(counterCreator);

                            var conditions = _ConditionCounterTemplateHelper.RefConditions.GetValue(templateConditions);

                            foreach (var condition2 in conditions)
                            {
                                switch (condition2)
                                {
                                    case ConditionVisitPlace place:
                                    {
                                        var zoneId = place.target;

                                        if (_ZoneHelper.TryGetValues(zoneId,
                                                out IEnumerable<ExperienceTrigger> triggers))
                                        {
                                            foreach (var trigger in triggers)
                                            {
                                                var staticModel = new StaticModel
                                                {
                                                    Id = id,
                                                    Where = trigger.transform.position,
                                                    ZoneId = zoneId,
                                                    NameKey = nameKey,
                                                    DescriptionKey = id,
                                                    TraderId = traderId,
                                                    IsNotNecessary = !counterCreator.IsNecessary,
                                                    InfoType = StaticModel.Type.ConditionVisitPlace
                                                };

                                                showStatic(staticModel);
                                            }
                                        }

                                        break;
                                    }
                                    case ConditionInZone inZone:
                                    {
                                        var zoneIds = inZone.zoneIds;

                                        foreach (var zoneId in zoneIds)
                                        {
                                            if (!_ZoneHelper.TryGetValues(zoneId,
                                                    out IEnumerable<ExperienceTrigger> triggers))
                                                continue;

                                            foreach (var trigger in triggers)
                                            {
                                                var staticModel = new StaticModel
                                                {
                                                    Id = id,
                                                    Where = trigger.transform.position,
                                                    ZoneId = zoneId,
                                                    NameKey = nameKey,
                                                    DescriptionKey = id,
                                                    TraderId = traderId,
                                                    IsNotNecessary = !counterCreator.IsNecessary,
                                                    InfoType = StaticModel.Type.ConditionInZone
                                                };

                                                showStatic(staticModel);
                                            }
                                        }

                                        break;
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private static void ShowExfiltration(Profile profile, string profileId, Action<StaticModel> showStatic)
        {
            var hudCoreModel = HUDCoreModel.Instance;

            var exfiltrationPoints = profile.Side != EPlayerSide.Savage
                ? hudCoreModel.TheWorld.ExfiltrationController.EligiblePoints(profile)
                : hudCoreModel.TheWorld.ExfiltrationController.ScavExfiltrationPoints
                    .Where(x => x.EligibleIds.Contains(profileId)).ToArray<ExfiltrationPoint>();

            for (var i = 0; i < exfiltrationPoints.Length; i++)
            {
                var point = exfiltrationPoints[i];

                var exfiltrationRequirements = new Func<bool>[]
                {
                    () => point.Status == EExfiltrationStatus.NotPresent,
                    () => point.Status == EExfiltrationStatus.UncompleteRequirements
                };

                var staticModel = new StaticModel
                {
                    Id = $"EXFIL{i}",
                    Where = point.transform.position,
                    NameKey = point.Settings.Name,
                    DescriptionKey = "EXFIL",
                    InfoType = StaticModel.Type.Exfiltration,
                    Requirements = exfiltrationRequirements
                };

                showStatic(staticModel);

                if (point.Status != EExfiltrationStatus.UncompleteRequirements)
                    continue;

                foreach (var @switch in _ExfiltrationPointHelper.RefSwitchs.GetValue(point))
                {
                    var switchStaticModel = new StaticModel
                    {
                        Id = @switch.Id,
                        Where = @switch.transform.position,
                        NameKey = point.Settings.Name,
                        DescriptionKey = @switch.ExtractionZoneTip,
                        InfoType = StaticModel.Type.Switch,
                        Requirements = exfiltrationRequirements.Concat(new Func<bool>[]
                        {
                            () => @switch.DoorState == EDoorState.Open
                        }).ToArray()
                    };

                    showStatic(switchStaticModel);
                }
            }
        }

#endif
    }
}