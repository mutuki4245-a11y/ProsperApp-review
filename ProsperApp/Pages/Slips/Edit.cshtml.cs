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
    ICheckoutRepository checkoutRepository,
    IStoreClock storeClock,
    IOrderQueueService orderQueueService) : PageModel
{
    private static readonly HashSet<string> AllowedNominationKinds = new(StringComparer.Ordinal)
    {
        "companion_18",
        "companion_19",
        "companion_20",
        "nomination",
        "in_store"
    };

    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly ICheckoutRepository _checkoutRepository = checkoutRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IOrderQueueService _orderQueueService = orderQueueService;

    private static readonly IReadOnlyList<CheckoutPaymentInputModel> PaymentTemplates =
    [
        new() { MethodCode = "cash", MethodName = "現金" },
        new() { MethodCode = "cat", MethodName = "CAT" },
        new() { MethodCode = "paypay", MethodName = "PAYPAY" }
    ];

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
    public CheckoutInputModel CheckoutInput { get; set; } = new();

    public SlipDetail? Detail { get; private set; }

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; private set; } = [];

    public IReadOnlyList<StoreOrderItemOption> OrderItems { get; private set; } = [];

    public IReadOnlyList<string> TimeOptions { get; private set; } = [];

    public string? SuccessMessage { get; private set; }

    public bool ShowOrderModal { get; private set; }

    public bool ShowCheckoutModal { get; private set; }

    public bool ShowAddCustomerModal { get; private set; }

    public bool ShowAddNominationModal { get; private set; }

    public bool ShowCashReceivedStep { get; private set; }

    public CheckoutTotals CheckoutTotals { get; private set; } = new();

    public bool CanCheckout => _featureGate.IsEnabled(FeatureNames.Checkout)
        && Detail is not null
        && string.Equals(Detail.Status, "open", StringComparison.Ordinal);

    public bool CanAddOrders => _featureGate.IsEnabled(FeatureNames.Orders)
        && Detail is not null
        && string.Equals(Detail.Status, "open", StringComparison.Ordinal);

    public bool IsOpenSlip => Detail is not null && string.Equals(Detail.Status, "open", StringComparison.Ordinal);

    public bool CanEditCustomerNames => Detail is not null
        && (string.Equals(Detail.Status, "open", StringComparison.Ordinal) ||
            string.Equals(Detail.Status, "checked_out", StringComparison.Ordinal));

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
