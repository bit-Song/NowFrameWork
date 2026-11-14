namespace NowFrameWork.Core
{
    public interface ITickable
    {
        void Tick(float deltaTime, float unscaledDeltaTime);
    }

    public interface ILateTickable
    {
        void LateTick();
    }

    public interface IFixedTickable
    {
        void FixedTick(float fixedDeltaTime);
    }
}