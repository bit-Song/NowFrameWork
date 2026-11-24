namespace NowFrameWork.Core.Fsm
{
    public interface IState
    {
        /// <summary>进入状态，初始化本次运行所需的数据。</summary>
        void Enter();

        /// <summary>执行每帧逻辑，由宿主的 Update 驱动。</summary>
        void Tick();

        /// <summary>执行物理更新逻辑，由宿主的 FixedUpdate 驱动。</summary>
        void FixedTick();

        /// <summary>退出状态，清理本次运行的资源。</summary>
        void Exit();
    }

    /// <summary>可选的退出协调接口；未实现的状态默认允许立即退出。</summary>
    public interface IStateExitHandler
    {
        /// <summary>普通切换能否退出；强制切换和停机会忽略此条件。</summary>
        bool CanExit { get; }

        /// <summary>普通退出被阻塞时通知一次，可在此开始收招或准备清理。</summary>
        void OnExitRequested();
    }

    /// <summary>为退出等待提供可读原因，供观察器显示。</summary>
    public interface IExitWaitDiagnostics
    {
        string ExitWaitReason { get; }
    }
}
