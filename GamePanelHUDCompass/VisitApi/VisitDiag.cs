using System;
using System.IO;
using System.Text;
using BepInEx;

namespace GamePanelHUDCompass.VisitApi
{
    /// <summary>
    /// 文件级诊断。日志可能被级别过滤或被其他插件刷掉，文件不会——
    /// 排查「整条链毫无动静」时，先确认代码到底有没有被执行到。
    ///
    /// 产物：BepInEx/plugins/kmyuhkyuk-GamePanelHUD/compass-visitdiag.txt
    /// </summary>
    internal static class VisitDiag
    {
        private static readonly object Gate = new object();

        private static string _path;

        private static bool _truncated;

        private static string DiagPath
        {
            get
            {
                if (_path != null)
                    return _path;

                try
                {
                    _path = Path.Combine(BepInEx.Paths.PluginPath, "kmyuhkyuk-GamePanelHUD",
                        "compass-visitdiag.txt");
                }
                catch (Exception)
                {
                    _path = "compass-visitdiag.txt";
                }

                return _path;
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    if (!_truncated)
                    {
                        _truncated = true;

                        try
                        {
                            File.WriteAllText(DiagPath, "=== GamePanelHUD Compass VisitAPI 诊断 ===\r\n",
                                Encoding.UTF8);
                        }
                        catch (Exception)
                        {
                            // 文件被占就继续往后追加
                        }
                    }

                    File.AppendAllText(DiagPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + "\r\n", Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 诊断本身绝不能让游戏崩
            }
        }
    }
}
