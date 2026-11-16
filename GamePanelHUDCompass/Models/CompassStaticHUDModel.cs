#if !UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GamePanelHUDCompass.Models
{
    internal class CompassStaticHUDModel
    {
        private static readonly Lazy<CompassStaticHUDModel> Lazy =
            new Lazy<CompassStaticHUDModel>(() => new CompassStaticHUDModel());

        public static CompassStaticHUDModel Instance => Lazy.Value;

        public bool CompassStaticHUDSw;

        public GameObject StaticPrefab;

        public int AirdropCount;

        public readonly CompassStaticModel CompassStatic = new CompassStaticModel();

        public Action<StaticModel> ShowStatic;

        public Action<string> DestroyStatic;

        // ---- VisitAPI 剧情区域（/visitapi/zones） ----

        /// <summary>本局已创建的区域标记 id 集合，用于增删对账</summary>
        public readonly HashSet<string> VisitZoneShownIds = new HashSet<string>();

        /// <summary>进入战局或任务状态变化时置位，触发一次区域表重建</summary>
        public bool VisitZonesDirty;

        /// <summary>上次重建的时间戳（秒），控制重建频率</summary>
        public float VisitZonesLastRefresh;

        private CompassStaticHUDModel()
        {
        }
    }
}

#endif
