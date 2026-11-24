using System.Collections.Concurrent;

namespace Sacred.GameServer.Lobby;

/// <summary>
/// Answers to hero uploads (message 18). sacred-lobby checks the whole block, stores it in one SQLite statement and then answers
/// message 15 <c>[code][18]</c>: 12 (PublicDataChanged) = stored, anything else = refused, slot unchanged. One lobby connection
/// answers in order, so every upload queues one entry here (in <see cref="UploadCharacter"/>, before its frame is queued) and every
/// answer completes the oldest. A lost connection fails every open entry (their frames are dropped with the outbox). Also keeps the
/// time of the last upload per lobby identity, across sessions, so a script can tell whether any save of a hero went out after a
/// given moment (<see cref="LastUploadMs"/>).
/// </summary>
public sealed partial class LobbyClient
{
    /// <summary>Code of message 15 for a stored data block.</summary>
    public const int UploadStored = 12;
    private readonly ConcurrentQueue<TaskCompletionSource<int>?> _uploadAnswers = new();
    private readonly ConcurrentDictionary<int, long> _lastUpload = new();
    private TaskCompletionSource<int>? _answerForNext;

    private void TrackUpload(int permId)
    {
        _lastUpload[permId] = Environment.TickCount64;
        _uploadAnswers.Enqueue(_answerForNext);
        _answerForNext = null;
    }

    private void UploadAnswered(int code)
    {
        if (_uploadAnswers.TryDequeue(out var answer)) answer?.TrySetResult(code);
    }

    private void FailUploads(string why)
    {
        while (_uploadAnswers.TryDequeue(out var answer)) answer?.TrySetException(new IOException(why));
    }

    /// <summary><see cref="Environment.TickCount64"/> of the last upload queued for <paramref name="permId"/>, or null.</summary>
    public long? LastUploadMs(int permId) => _lastUpload.TryGetValue(permId, out var at) ? at : null;

    /// <summary>Uploads a hero block like <see cref="UploadCharacter"/> and completes once the lobby answered: null when it stored
    /// the block, else why not (refused, connection lost, no answer within <paramref name="timeout"/>). Call on the game loop.</summary>
    public Task<string?> UploadCharacterAcked(int permId, ushort block, ReadOnlySpan<byte> data, TimeSpan timeout)
    {
        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answerForNext = answer;
        if (!UploadCharacter(permId, block, data)) { _answerForNext = null; return Task.FromResult<string?>("the hero block has an impossible size"); }
        return Wait(answer.Task, timeout);

        static async Task<string?> Wait(Task<int> task, TimeSpan timeout)
        {
            var finished = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (finished != task) return "the lobby did not answer";
            if (task.IsFaulted) return task.Exception!.GetBaseException().Message;
            return task.Result == UploadStored ? null : $"the lobby refused the hero (result {task.Result})";
        }
    }
}
