namespace MddBooster.Tests.Cli;

/// <summary>
/// Serialises the tests that capture console output.
/// </summary>
/// <remarks>
/// <c>Console.SetOut</c>/<c>SetError</c> replace process-wide state, so two
/// tests capturing at once interleave: one restores the original writer while
/// the other is still recording, and the second one's assertion then reads an
/// empty buffer. Test classes are separate collections by default and therefore
/// run in parallel, which made this surface as an intermittent failure —
/// roughly one run in five — with no relation to the change under test.
/// Every class that captures the console belongs in this collection.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCaptureCollection
{
    public const string Name = "console-capture";
}

/// <summary>
/// Captures <c>Console.Error</c> for the lifetime of the instance and restores
/// it on dispose. Refuses to start unless the calling test class belongs to
/// <see cref="ConsoleCaptureCollection"/>.
/// </summary>
/// <remarks>
/// Serialising the collection fixed the race, but nothing stopped a later test
/// class from capturing the console without joining it — and the resulting
/// failure would again be intermittent and point anywhere but the cause. The
/// check here turns that omission into an immediate failure that names the fix,
/// on the first run rather than on an unlucky one.
/// <para>
/// This detects rather than prevents: a test that calls
/// <c>Console.SetError</c> directly still bypasses it. Scanning the test
/// assembly's IL for those calls would close that gap, but needs an opcode
/// length table to avoid flagging methods whose operand bytes merely look like
/// a call — disproportionate for the risk, given every existing capture site
/// goes through this type and a new one is written by copying an old one.
/// </para>
/// </remarks>
public sealed class ConsoleErrorCapture : IDisposable
{
    private readonly TextWriter _previous;
    private readonly StringWriter _captured = new();

    public ConsoleErrorCapture(object callingTestInstance)
    {
        ArgumentNullException.ThrowIfNull(callingTestInstance);

        var type = callingTestInstance.GetType();
        // CollectionAttribute does not expose its name as a property in this xUnit
        // version, so the constructor argument is read from the metadata instead.
        var collectionName = type.GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(CollectionAttribute))
            .SelectMany(d => d.ConstructorArguments)
            .Select(a => a.Value as string)
            .FirstOrDefault();

        if (collectionName != ConsoleCaptureCollection.Name)
        {
            throw new InvalidOperationException(
                $"{type.Name} captures console output but is not in the " +
                $"'{ConsoleCaptureCollection.Name}' collection. Console.SetOut/SetError replace " +
                "process-wide state, so an uncollected class races every other capturing test and " +
                $"fails intermittently. Add [Collection(ConsoleCaptureCollection.Name)] to {type.Name}.");
        }

        _previous = Console.Error;
        Console.SetError(_captured);
    }

    public string Text => _captured.ToString();

    public void Dispose() => Console.SetError(_previous);
}
