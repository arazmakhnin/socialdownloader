using CommandLine;

[Verb("vk", HelpText = "Run the VK group downloading")]
public class VkOptions
{
    [Option("group", Required = true, HelpText = "Group to read")]
    public string GroupName { get; set; } = string.Empty;
}