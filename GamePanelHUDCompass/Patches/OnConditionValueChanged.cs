#if !UNITY_EDITOR

using EFT.Quests;
using GamePanelHUDCompass.Models;

namespace GamePanelHUDCompass
{
    public partial class GamePanelHUDCompassPlugin
    {
        private static void OnConditionValueChanged(EQuestStatus status,
            Condition condition)
        {
            if (status != EQuestStatus.Started)
            {
                CompassStaticHUDModel.Instance.DestroyStatic(condition.id);
            }

            // VisitAPI 剧情区域标记的 id 形如 "visit:zoneId:conditionId"，
            // 任务状态一变就让下一轮对账重建，避免已完成的区域继续挂在罗盘上。
            CompassStaticHUDModel.Instance.VisitZonesDirty = true;
        }
    }
}

#endif
