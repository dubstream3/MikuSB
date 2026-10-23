using MikuSB.Enums.Player;

namespace MikuSB.GameServer.Command;

[AttributeUsage(AttributeTargets.Class)]
public class CommandInfoAttribute(
    string name, string desc, string usage, string[] alias, PermEnum[] perm) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = desc;
    public string Usage { get; } = usage;
    public PermEnum[] Perm { get; } = perm;
    public string[] Alias { get; } = alias;
}

[AttributeUsage(AttributeTargets.Method)]
public class CommandDefaultAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public class CommandMethodAttribute(string method) : Attribute
{
    public string MethodName { get; } = method;
}