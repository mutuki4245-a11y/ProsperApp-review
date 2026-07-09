using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProsperApp.Models;
using ProsperApp.Options;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel(
    IFeatureGate featureGate,
    IStoreSlipRepository slipRepository,
    IBusinessDayRepository businessDayRepository,
    IStoreOrderRepository orderRepository,
    INominationBackAdminRepository nominationBackRepository,
    ICheckoutRepository checkoutRepository,
    IStoreClock storeClock,
    IOrderQueueService orderQueueService,
    IOptions<ReceiptPrinterOptions> receiptPrinterOptions,
    ILocalSettingsProvider localSettingsProvider) : PageModel
{
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IStoreSlipRepository _slipRepository = slipRepository;
    private readonly IBusinessDayRepository _businessDayRepository = businessDayRepository;
    private readonly IStoreOrderRepository _orderRepository = orderRepository;
    private readonly INominationBackAdminRepository _nominationBackRepository = nominationBackRepository;
    private readonly ICheckoutRepository _checkoutRepository = checkoutRepository;
    private readonly IStoreClock _storeClock = storeClock;
    private readonly IOrderQueueService _orderQueueService = orderQueueService;
    private readonly ReceiptPrinterOptions _receiptPrinterOptions = receiptPrinterOptions.Value;
    private readonly ILocalSettingsProvider _localSettingsProvider = localSettingsProvider;

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

    [BindProperty]
    public SaveSlipAdjustmentsInputModel AdjustmentsInput { get; set; } = new();

    [BindProperty]
    public List<KaraokeQuantityInputModel> KaraokeLines { get; set; } = [];

    public SlipDetail? Detail { get; private set; }

    public StoreBusinessDay? CurrentBusinessDay { get; private set; }

    public IReadOnlyList<StoreOrderAttendanceCastOption> AttendanceCasts { get; private set; } = [];

    public IReadOnlyList<StoreOrderItemOption> OrderItems { get; private set; } = [];

    public IReadOnlyList<NominationBackMasterItem> NominationOptions { get; private set; } = [];

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
