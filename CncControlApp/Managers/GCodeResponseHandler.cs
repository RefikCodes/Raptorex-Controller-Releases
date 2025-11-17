using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// GRBL Response Handler - Response beklemek için kullanılır
/// </summary>
public class GCodeResponseHandler : IDisposable
{
    private readonly ConcurrentQueue<string> _responseQueue = new ConcurrentQueue<string>();
    private readonly SemaphoreSlim _responseSemaphore = new SemaphoreSlim(0);
    private volatile bool _disposed = false;

    /// <summary>
    /// Response received event handler
    /// </summary>
    public void OnResponseReceived(string response)
    {
        if (_disposed) return;

        try
        {
            _responseQueue.Enqueue(response);
            _responseSemaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposal errors
        }
    }

    /// <summary>
    /// Response bekle (timeout ile)
    /// </summary>
    /// <param name="timeoutMs">Timeout süres (ms)</param>
    /// <returns>Response string veya null (timeout)</returns>
    public async Task<string> WaitForResponseAsync(int timeoutMs = 5000)
    {
        if (_disposed) return null;

        try
        {
            // Wait for response with timeout
            bool hasResponse = await _responseSemaphore.WaitAsync(timeoutMs);
            
            if (hasResponse && _responseQueue.TryDequeue(out string response))
            {
                return response;
            }
            
            return null; // Timeout or no response
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Response bekle (timeout ile, iptal edilebilir)
    /// </summary>
    /// <param name="timeoutMs">Timeout süres (ms)</param>
    /// <param name="cancellationToken">İptal jetonu</param>
    /// <returns>Response string veya null (timeout veya iptal)</returns>
    public async Task<string> WaitForResponseAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        if (_disposed) return null;

        try
        {
            // Wait for response with timeout and cancellation support
            bool hasResponse = await _responseSemaphore.WaitAsync(timeoutMs, cancellationToken);
            
            if (hasResponse && _responseQueue.TryDequeue(out string response))
            {
                return response;
            }
            
            return null; // Timeout, no response, or cancelled
        }
        catch (OperationCanceledException)
        {
            return null; // Return null on cancellation
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resources'ları temizle
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _responseSemaphore?.Dispose();
        }
        catch { }
    }
}