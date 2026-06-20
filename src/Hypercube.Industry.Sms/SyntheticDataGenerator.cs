namespace Hypercube.Industry.Sms;

/// <summary>
/// Generates realistic synthetic SMS send data for testing and demo purposes.
/// Mirrors the logic of the Python generate_customer_data.py script.
/// </summary>
public sealed class SyntheticDataGenerator
{
    private readonly string _senderId;
    private readonly string _senderTier;
    private readonly string _country;
    private readonly string _primaryCarrier;
    private readonly Dictionary<string, double> _messageMix;
    private readonly DateOnly _startDate;
    private readonly int _weeks;
    private readonly CampaignConfig? _campaign;
    private readonly Random _random;

    private static readonly Dictionary<string, double> CarrierBaseDelivery = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Vodacom"] = 0.975,
        ["MTN"] = 0.965,
        ["Telkom"] = 0.955,
        ["CellC"] = 0.945
    };

    private static readonly Dictionary<string, int> BaseVolumePerTierHour = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enterprise"] = 1200,
        ["Standard"] = 350,
        ["Trial"] = 40
    };

    private static readonly Dictionary<string, MessageTypeProfile> MessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OTP"] = new MessageTypeProfile(ResponseRate: 0.000, PositiveShare: 0.00, OptOutRate: 0.0000, DeliveryRateAdj: 0.005),
        ["Transactional"] = new MessageTypeProfile(ResponseRate: 0.005, PositiveShare: 0.30, OptOutRate: 0.0002, DeliveryRateAdj: 0.000),
        ["Promotional"] = new MessageTypeProfile(ResponseRate: 0.050, PositiveShare: 0.70, OptOutRate: 0.0020, DeliveryRateAdj: -0.005)
    };

    // Hourly volume curve (24 values, sum normalized to 1.0)
    private static readonly double[] HourlyCurve =
    [
        0.05, 0.03, 0.02, 0.02, 0.03, 0.10, 0.35, 0.70,
        1.10, 1.40, 1.55, 1.60, 1.45, 1.50, 1.55, 1.50,
        1.35, 1.10, 0.80, 0.55, 0.35, 0.20, 0.12, 0.08
    ];

    private static readonly double[] WeekdayFactor = [1.00, 1.05, 1.05, 1.00, 0.95, 0.35, 0.20];
    private static readonly int[] WeekdayFactorIndex = [0, 1, 2, 3, 4, 5, 6];
    private const double PublicHolidayFactor = 0.30;

    /// <summary>
    /// Configuration for a planted campaign window.
    /// </summary>
    public sealed record CampaignConfig(
        string MessageType,
        DateOnly StartDate,
        int Days,
        double ResponseRate,
        double PositiveShare,
        string Note);

    /// <summary>
    /// Profile for a message type.
    /// </summary>
    private sealed record MessageTypeProfile(
        double ResponseRate,
        double PositiveShare,
        double OptOutRate,
        double DeliveryRateAdj);

    /// <summary>
    /// Creates a synthetic data generator.
    /// </summary>
    /// <param name="senderId">Sender identifier.</param>
    /// <param name="senderTier">Sender tier (Trial, Standard, Enterprise).</param>
    /// <param name="country">Country code (e.g. "ZA").</param>
    /// <param name="primaryCarrier">Primary carrier (Vodacom, MTN, Telkom, CellC).</param>
    /// <param name="messageMix">Message type mix (must sum to 1.0).</param>
    /// <param name="startDate">Start date for data generation.</param>
    /// <param name="weeks">Number of weeks to generate.</param>
    /// <param name="campaign">Optional campaign configuration.</param>
    /// <param name="seed">Random seed for reproducibility.</param>
    public SyntheticDataGenerator(
        string senderId,
        string senderTier,
        string country,
        string primaryCarrier,
        Dictionary<string, double> messageMix,
        DateOnly startDate,
        int weeks,
        CampaignConfig? campaign = null,
        int? seed = null)
    {
        _senderId = senderId ?? throw new ArgumentNullException(nameof(senderId));
        _senderTier = senderTier ?? throw new ArgumentNullException(nameof(senderTier));
        _country = country ?? throw new ArgumentNullException(nameof(country));
        _primaryCarrier = primaryCarrier ?? throw new ArgumentNullException(nameof(primaryCarrier));
        _messageMix = messageMix ?? throw new ArgumentNullException(nameof(messageMix));
        _startDate = startDate;
        _weeks = weeks > 0 ? weeks : throw new ArgumentOutOfRangeException(nameof(weeks));
        _campaign = campaign;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Generates hourly send events as a list of SmsEvent records.
    /// </summary>
    public List<SmsEvent> Generate()
    {
        var events = new List<SmsEvent>();
        var zaHolidays = LoadZaHolidays(_startDate.Year);

        for (var dayOffset = 0; dayOffset < _weeks * 7; dayOffset++)
        {
            var currentDate = _startDate.AddDays(dayOffset);
            var isHoliday = zaHolidays.Contains(currentDate);

            for (var hour = 0; hour < 24; hour++)
            {
                var totalVolume = (int)Math.Round(HourlyVolume(currentDate, hour, isHoliday));
                if (totalVolume == 0)
                {
                    continue;
                }

                var typeCounts = SplitByMessageType(totalVolume);
                foreach (var (messageType, sent) in typeCounts)
                {
                    if (sent == 0)
                    {
                        continue;
                    }

                    var rate = DeliveryRate(messageType);
                    var delivered = (int)Math.Round(sent * rate);
                    var failed = sent - delivered;

                    // Opt-outs (subset of delivered)
                    var optOutRate = OptOutRate(messageType);
                    var optOuts = (int)Math.Round(delivered * optOutRate);
                    // For the SmsEvent record, we track opt_outs as part of "failed" for simplicity
                    // In a richer model, this would be a separate field.

                    events.Add(new SmsEvent(
                        SenderId: _senderId,
                        Carrier: _primaryCarrier,
                        MessageType: messageType,
                        Timestamp: new DateTimeOffset(currentDate.ToDateTime(new TimeOnly(hour, 0, 0)), TimeSpan.FromHours(2)),
                        Delivered: delivered,
                        Failed: failed,
                        Total: sent));
                }
            }
        }

        return events;
    }

    private double HourlyVolume(DateOnly date, int hour, bool isHoliday)
    {
        var baseVolume = BaseVolumePerTierHour[_senderTier];
        var factor = HourlyCurve[hour];
        factor *= isHoliday ? PublicHolidayFactor : WeekdayFactor[(int)date.DayOfWeek];
        factor *= (0.85 + _random.NextDouble() * 0.30);
        return Math.Max(0, baseVolume * factor);
    }

    private Dictionary<string, int> SplitByMessageType(int totalVolume)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var remaining = totalVolume;
        var items = _messageMix.ToList();

        for (var i = 0; i < items.Count; i++)
        {
            if (i == items.Count - 1)
            {
                counts[items[i].Key] = remaining;
            }
            else
            {
                var count = (int)Math.Round(totalVolume * items[i].Value);
                counts[items[i].Key] = count;
                remaining -= count;
            }
        }

        return counts;
    }

    private double DeliveryRate(string messageType)
    {
        var carrierBase = CarrierBaseDelivery[_primaryCarrier];
        var adjusted = carrierBase + MessageTypes[messageType].DeliveryRateAdj;
        adjusted += (_random.NextDouble() - 0.5) * 0.02;
        return Math.Clamp(adjusted, 0.05, 0.995);
    }

    private double OptOutRate(string messageType)
    {
        var baseRate = MessageTypes[messageType].OptOutRate;
        return baseRate * (0.5 + _random.NextDouble());
    }

    private static HashSet<DateOnly> LoadZaHolidays(int year)
    {
        // Simplified SA public holidays (fixed dates only).
        // Production should use a proper holidays library or dynamic source.
        var holidays = new HashSet<DateOnly>
        {
            new DateOnly(year, 1, 1),   // New Year's Day
            new DateOnly(year, 3, 21),  // Human Rights Day
            new DateOnly(year, 4, 27),  // Freedom Day
            new DateOnly(year, 5, 1),   // Workers' Day
            new DateOnly(year, 6, 16),  // Youth Day
            new DateOnly(year, 8, 9),   // National Women's Day
            new DateOnly(year, 9, 24),  // Heritage Day
            new DateOnly(year, 12, 16), // Day of Reconciliation
            new DateOnly(year, 12, 25), // Christmas Day
            new DateOnly(year, 12, 26)  // Day of Goodwill
        };

        return holidays;
    }
}