namespace CsvToolkit.Sample.Infrastructure;

public sealed record SamplePaths(
    string DataDirectory,
    string PeoplePath,
    string EmployeesPath,
    string OutputDirectory,
    string PeopleExportPath,
    string AsyncExportPath)
{
    public static SamplePaths FromBaseDirectory(string baseDirectory)
    {
        var dataDirectory = Path.Combine(baseDirectory, "data");
        var outputDirectory = Path.Combine(baseDirectory, "output");

        return new SamplePaths(
            dataDirectory,
            Path.Combine(dataDirectory, "people.csv"),
            Path.Combine(dataDirectory, "employees_fluent.csv"),
            outputDirectory,
            Path.Combine(outputDirectory, "people-export.csv"),
            Path.Combine(outputDirectory, "people-export-async.csv"));
    }
}