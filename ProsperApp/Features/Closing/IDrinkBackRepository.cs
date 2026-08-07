using ProsperApp.Features.Shared;

namespace ProsperApp.Features.Closing;

public interface IDrinkBackRepository
{
    Task<Result<CurrentDrinkBackEditorState>> GetCurrentEditorAsync(CancellationToken ct);

    Task<Result<DrinkBackSaveOutput>> SaveAsync(DrinkBackSaveMutation input, CancellationToken ct);
}
