using System.Collections;
using UnityEngine;

/// <summary>
/// 命中時に一瞬だけ時間を止める演出。多重に呼ばれても伸びないようにしてある。
/// </summary>
public static class HitStop
{
    static Runner _runner;
    static bool _running;

    public static void Play(float durationSeconds)
    {
        if (_running) return;

        if (_runner == null)
        {
            var go = new GameObject("~HitStopRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }

        _runner.StartCoroutine(Freeze(durationSeconds));
    }

    static IEnumerator Freeze(float durationSeconds)
    {
        _running = true;
        float restore = Time.timeScale;

        Time.timeScale = 0.02f;
        yield return new WaitForSecondsRealtime(durationSeconds);
        Time.timeScale = restore <= 0.05f ? 1f : restore;

        _running = false;
    }

    class Runner : MonoBehaviour { }
}
