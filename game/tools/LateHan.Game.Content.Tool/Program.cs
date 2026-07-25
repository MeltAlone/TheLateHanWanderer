using LateHan.Game.Content;

if (args.Length != 1)
{
    Console.Error.WriteLine("用法：LateHan.Game.Content.Tool <场景数据路径>");
    return 2;
}

try
{
    var document = ScenarioJsonLoader.ReadDocument(args[0]);
    Console.WriteLine($"场景数据有效：{document.Name}（内容版本 {document.ContentVersion}）");
    return 0;
}
catch (ScenarioDataException exception)
{
    foreach (var error in exception.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}
