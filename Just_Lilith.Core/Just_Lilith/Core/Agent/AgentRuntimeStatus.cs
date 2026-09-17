namespace Just_Lilith.Core.Agent;

public sealed record AgentRuntimeStatus(string State, string Detail = "")
{
	public string Label => State switch
	{
		"disabled" => "已关闭", 
		"idle" => "待命", 
		"starting" => "启动中", 
		"working" => "工作中", 
		"command" => "执行命令", 
		"editing" => "编辑文件", 
		"responding" => "回复中", 
		"completed" => "已完成", 
		"cancelled" => "已取消", 
		"failed" => "失败", 
		_ => State, 
	};
}
