using CsvToolkit.Sample.Demos;
using CsvToolkit.Sample.Infrastructure;

var paths = SamplePaths.FromBaseDirectory(AppContext.BaseDirectory);
Directory.CreateDirectory(paths.OutputDirectory);

Console.WriteLine("=== CsvToolkit.Core Sample ===");
Console.WriteLine($"Data directory: {paths.DataDirectory}");

var options = CsvOptionsFactory.Create();

RowApiDemo.Run(paths.PeoplePath, options);
DictionaryApiDemo.Run(paths.PeoplePath, options);
DynamicApiDemo.Run(paths.PeoplePath, options);
AttributeRecordApiDemo.Run(paths.PeoplePath, options);
FluentMapApiDemo.Run(paths.EmployeesPath, options);
WriteApiDemo.Run(paths.PeopleExportPath, options);
await AsyncApiDemo.RunAsync(paths.PeoplePath, paths.AsyncExportPath, options);

Console.WriteLine("=== Sample Completed ===");