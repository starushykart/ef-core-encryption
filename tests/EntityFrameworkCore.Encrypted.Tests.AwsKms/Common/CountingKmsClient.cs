using System.Collections.Concurrent;
using System.Reflection;
using Amazon.KeyManagementService;

namespace EntityFrameworkCore.Encrypted.Tests.AwsKms.Common;

/// <summary>Forwards to a real KMS client and counts calls per operation.</summary>
public class CountingKmsClient : DispatchProxy
{
    private IAmazonKeyManagementService _inner = null!;

    public ConcurrentDictionary<string, int> Calls { get; } = new();

    public int this[string operation] => Calls.GetValueOrDefault(operation);

    public static (IAmazonKeyManagementService Client, CountingKmsClient Counter) Wrap(IAmazonKeyManagementService inner)
    {
        var client = Create<IAmazonKeyManagementService, CountingKmsClient>();
        var counter = (CountingKmsClient)(object)client;
        counter._inner = inner;
        return (client, counter);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var operation = targetMethod!.Name.Replace("Async", string.Empty);
        Calls.AddOrUpdate(operation, 1, (_, count) => count + 1);

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }
}
