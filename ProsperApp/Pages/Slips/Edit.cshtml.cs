using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel(
    IFeatureGate featureGate,
    IStoreSlipRepository slipRepository,
    IBusinessDayRepository businessDayRepository,
    IStoreOrderRepository orderRepository,
    INominationBackAdminRepository nominationBackRepository,
    IStoreClock storeClock,
    IOrderQueueService orderQueueService) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly INominationBackAdminRepository _nominationBackRepository = nominationBackRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IOrderQueueService _orderQueueService = orderQueueService;

    [BindProperty(SupportsGet = true)]
    public long? SlipId { get; set; }

    [BindProperty]
    public AddSlipCustomersInputModel AddCustomersInput { get; set; } = new();

    [BindProperty]
    public AddSlipNominationsInputModel AddNominationsInput { get; set; } = new();

    [BindProperty]
    public LeaveSlipCustomerInputModel LeaveCustomerInput { get; set; } = new();

    [BindProperty]
    public UpdateSlipCustomerInputModel UpdateCustomerInput { get; set; } = new();

    [BindProperty]
    public List<OrderQueueInputModel> QueueLines { get; set; } = [];

    [BindProperty]
    public string OrderQueueJson { get; set; } = string.Empty;

    [BindProperty]
    public SlipAdjustmentInputModel AdjustmentInput { get; set; } = new();

    [BindProperty]
    public List<OrderLineQuantityInputModel> OrderQuantityLines { get; set; } = [];

    public SlipDetail? Detail { get; private set; }

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; private set; } = [];

    public IReadOnlyList<StoreOrderItemOption> OrderItems { get; private set; } = [];

    public IReadOnlyList<NominationBackMasterItem> NominationOptions { get; private set; } = [];

    public IReadOnlyList<string> TimeOptions { get; private set; } = [];

    public string? SuccessMessage { get; private set; }

    public bool ShowOrderModal { get; private set; }

    public bool ShowAddCustomerModal { get; private set; }

    public bool ShowAddNominationModal { get; private set; }

    public bool ShowAdjustmentModal { get; private set; }

    public bool CanAddOrders => _featureGate.IsEnabled(FeatureNames.Orders)
        && Detail is not null
        && string.Equals(Detail.Status, "open", StringComparison.Ordinal);

    public bool IsOpenSlip => Detail is not null && string.Equals(Detail.Status, "open", StringComparison.Ordinal);

    public bool CanEditCustomerNames => Detail is not null &&
        string.Equals(Detail.Status, "open", StringComparison.Ordinal);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }

}
