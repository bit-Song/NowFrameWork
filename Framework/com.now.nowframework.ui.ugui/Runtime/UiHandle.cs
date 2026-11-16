using System;

namespace NowFrameWork.UI
{
    /// <summary>
    /// 界面实例句柄：非拥有型，不自动释放资源。
    ///
    /// `Session` 是会话代数：界面关闭后进缓存、再打开时分配新代数，
    /// 旧句柄因此自动失效，不会误操作新会话。
    /// </summary>
    public readonly struct UiHandle : IEquatable<UiHandle>
    {
        public UiHandle(int owner, int id, int session)
        {
            Owner = owner;
            Id = id;
            Session = session;
        }

        public int Owner { get; }

        public int Id { get; }

        public int Session { get; }

        public bool IsValid => Id > 0 && Session > 0;

        public bool Equals(UiHandle other) => Owner == other.Owner && Id == other.Id && Session == other.Session;

        public override bool Equals(object obj) => obj is UiHandle other && Equals(other);

        public override int GetHashCode() => (Owner * 397) ^ (Id * 31) ^ Session;

        public static bool operator ==(UiHandle left, UiHandle right) => left.Equals(right);

        public static bool operator !=(UiHandle left, UiHandle right) => !left.Equals(right);

        public override string ToString() => "Ui(" + Owner + "/" + Id + "@" + Session + ")";
    }
}