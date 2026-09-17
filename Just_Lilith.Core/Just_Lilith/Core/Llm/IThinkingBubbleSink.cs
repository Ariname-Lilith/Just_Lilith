namespace Just_Lilith.Core.Llm;

/// <summary>
/// 可选能力：在用户发出输入、但模型回复尚未返回时，先在气泡里显示“少女思考中....”。
/// 输出桥可以选择实现该接口；未实现时对话流程保持原样。
/// </summary>
public interface IThinkingBubbleSink
{
	/// <summary>用户输入已提交，开始等待模型输出。</summary>
	void ShowThinking();

	/// <summary>本轮请求已经结束（收到回复或失败/取消）。收到回复时气泡会保留到正式回复接替。</summary>
	void EndThinking();
}
