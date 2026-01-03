namespace League.States
{
    /// <summary>
    /// ARAM 自动换英雄的状态容器（用于在异步方法间传递可变状态）
    /// </summary>
    public class AramAutoSwapState
    {
        public int UserPreferredChampId { get; set; } = 0;     // 用户当前偏好的英雄ID
        public bool StopAutoSwap { get; set; } = false;        // 是否本局停止自动换
    }
}