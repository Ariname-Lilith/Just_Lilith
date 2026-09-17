namespace Just_Lilith.Core.Llm;

public interface IOrdinaryChatOutputSink
{
	void Publish(OrdinaryChatOutput output);
}
