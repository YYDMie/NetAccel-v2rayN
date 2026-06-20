using NetAccel.Managed.Runtime;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        return args.FirstOrDefault()?.ToLowerInvariant() switch
        {
            "try" => await TryAcquireAsync(args),
            "hold" => await HoldAsync(args),
            "switch" => await SwitchAsync(args),
            _ => Usage(),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR type={ex.GetType().Name}");
        return 70;
    }
}

static async Task<int> TryAcquireAsync(string[] args)
{
    if (args.Length != 4 || !TryParseOwner(args[1], out var owner))
    {
        return Usage();
    }

    await using var coordinator = CreateCoordinator(args[2], args[3]);
    var result = await coordinator.AcquireAsync(owner);
    if (!result.Acquired || result.Lease == null)
    {
        Console.WriteLine($"BLOCKED owner={owner} reason={result.ConflictReason}");
        return 2;
    }

    await result.Lease.DisposeAsync();
    Console.WriteLine($"ACQUIRED owner={owner}");
    return 0;
}

static async Task<int> HoldAsync(string[] args)
{
    if (args.Length is < 6 or > 7 || !TryParseOwner(args[1], out var owner))
    {
        return Usage();
    }

    var timeoutSeconds = args.Length == 7 && int.TryParse(args[6], out var parsedTimeout)
        ? parsedTimeout
        : 30;
    await using var coordinator = CreateCoordinator(args[2], args[3]);
    var result = await coordinator.AcquireAsync(owner);
    if (!result.Acquired || result.Lease == null)
    {
        Console.WriteLine($"BLOCKED owner={owner} reason={result.ConflictReason}");
        return 2;
    }

    File.WriteAllText(args[4], $"{Environment.ProcessId}:{owner}");
    Console.WriteLine($"HOLDING owner={owner} pid={Environment.ProcessId}");
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (!File.Exists(args[5]))
    {
        if (DateTime.UtcNow >= deadline)
        {
            Console.Error.WriteLine($"TIMEOUT owner={owner}");
            return 3;
        }

        await Task.Delay(50);
    }

    await result.Lease.DisposeAsync();
    Console.WriteLine($"RELEASED owner={owner}");
    return 0;
}

static async Task<int> SwitchAsync(string[] args)
{
    if (args.Length is < 3 or > 4)
    {
        return Usage();
    }

    var iterations = args.Length == 4 && int.TryParse(args[3], out var parsedIterations)
        ? parsedIterations
        : 10;
    await using var coordinator = CreateCoordinator(args[1], args[2]);
    for (var i = 0; i < iterations; i++)
    {
        if (!await AcquireAndReleaseAsync(coordinator, ConnectionOwner.Managed))
        {
            return 4;
        }

        if (!await AcquireAndReleaseAsync(coordinator, ConnectionOwner.Classic))
        {
            return 4;
        }
    }

    Console.WriteLine($"SWITCHED iterations={iterations}");
    return 0;
}

static async Task<bool> AcquireAndReleaseAsync(
    ConnectionOwnershipCoordinator coordinator,
    ConnectionOwner owner)
{
    var result = await coordinator.AcquireAsync(owner);
    if (!result.Acquired || result.Lease == null)
    {
        Console.Error.WriteLine($"SWITCH_FAILED owner={owner} reason={result.ConflictReason}");
        return false;
    }

    await result.Lease.DisposeAsync();
    return true;
}

static ConnectionOwnershipCoordinator CreateCoordinator(string snapshotPath, string mutexName)
    => new(new FileConnectionOwnershipStore(snapshotPath), mutexName: mutexName);

static bool TryParseOwner(string value, out ConnectionOwner owner)
    => Enum.TryParse(value, ignoreCase: true, out owner) && owner != ConnectionOwner.None;

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  try <Managed|Classic> <snapshot-path> <mutex-name>");
    Console.Error.WriteLine("  hold <Managed|Classic> <snapshot-path> <mutex-name> <ready-path> <release-path> [timeout-seconds]");
    Console.Error.WriteLine("  switch <snapshot-path> <mutex-name> [iterations]");
    return 64;
}
