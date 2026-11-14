namespace NowFrameWork.Core
{
    /// <summary>
    /// 进度缩放装饰器：把子流程的 0..1 映射到外层的一段区间。
    /// 用于组合启动阶段，例如「模块启动占总进度的 0–80%，资源预热占 80–100%」。
    /// 不分配：除构造外不再产生对象。
    /// </summary>
    public sealed class ScaledProgress : IStartupProgress
    {
        readonly IStartupProgress _inner;
        readonly float _from;
        readonly float _to;

        public ScaledProgress(IStartupProgress inner, float from, float to)
        {
            _inner = inner;
            _from = from;
            _to = to;
        }

        public void Phase(string name) => _inner?.Phase(name);

        public void Progress(float value01)
        {
            if (value01 < 0f) value01 = 0f;
            else if (value01 > 1f) value01 = 1f;

            _inner?.Progress(_from + (_to - _from) * value01);
        }

        public void Failed(string phase, string moduleId) => _inner?.Failed(phase, moduleId);
    }
}