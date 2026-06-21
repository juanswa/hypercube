namespace Hypercube.Tui.Demo;

using global::Hypercube.Industry;
using global::Hypercube.Industry.Sms;

internal sealed class SmsCampaignGenerator
{
    private readonly Random _random;
    private readonly string[] _carriers = { "Vodacom", "MTN", "CellC", "Telkom" };
    private readonly string[] _messageTypes = { "OTP", "Transactional", "Promotional" };

    private sealed record EngagementProfile(double ReplyRate, double OptOutRate);

    public SmsCampaignGenerator(int seed = 42)
    {
        _random = new Random(seed);
    }

    /// <summary>
    /// Generates N SMS messages for a subject across a window, with an anomaly in the final third.
    /// </summary>
    public IEnumerable<SmsEvent> Generate(
        ISubject subject,
        int count,
        DateTimeOffset windowStart,
        TimeSpan windowDuration)
    {
        var windowSeconds = windowDuration.TotalSeconds;

        // Anomaly: (MTN, Promotional) gets degraded delivery in final third
        var anomalyCarrier = "MTN";
        var anomalyType = "Promotional";
        var anomalyStartIndex = count * 2 / 3;

        for (int i = 0; i < count; i++)
        {
            var carrier = _carriers[_random.Next(_carriers.Length)];
            var messageType = _messageTypes[_random.Next(_messageTypes.Length)];

            // Timestamp spread across window, varying dow/hod buckets
            var fractionThrough = i / (double)count;
            var timestamp = windowStart.AddSeconds(fractionThrough * windowSeconds);

            // Baseline status mix aligned to requested demo guidance:
            // DELIVRD majority, EXPIRED around 5%, SPAM around 0.5%, with a REJECTD spike anomaly
            // on MTN|Promotional in the final third.
            var isAnomaly = i > anomalyStartIndex && carrier == anomalyCarrier && messageType == anomalyType;
            var engagement = GetEngagementProfile(messageType, isAnomaly);
            var expiredWeight = Math.Max(0d, 0.050 + ((_random.NextDouble() - 0.5) * 0.010));
            var undelivWeight = Math.Max(0d, 0.010 + ((_random.NextDouble() - 0.5) * 0.004));
            var rejectdWeight = Math.Max(0d, (isAnomaly ? 0.30 : 0.010) + ((_random.NextDouble() - 0.5) * (isAnomaly ? 0.020 : 0.004)));
            var spamWeight = Math.Max(0d, 0.005 + ((_random.NextDouble() - 0.5) * 0.002));
            var cancelledWeight = Math.Max(0d, 0.005 + ((_random.NextDouble() - 0.5) * 0.002));

            // total is 1, so select exactly one final state by weighted sampling to preserve
            // realistic outcomes and keep report totals aligned to --campaign count semantics.
            var nonDeliveredWeight = expiredWeight + undelivWeight + rejectdWeight + spamWeight + cancelledWeight;
            var deliveredWeight = Math.Max(0d, 1d - nonDeliveredWeight);
            var weightSum = deliveredWeight + nonDeliveredWeight;

            long delivered;
            long expired;
            long undeliv;
            long rejectd;
            long spam;
            long cancelled;

            if (weightSum <= 0d)
            {
                delivered = 1;
                expired = 0;
                undeliv = 0;
                rejectd = 0;
                spam = 0;
                cancelled = 0;
            }
            else
            {
                var pick = _random.NextDouble() * weightSum;
                var threshold = deliveredWeight;

                delivered = 0;
                expired = 0;
                undeliv = 0;
                rejectd = 0;
                spam = 0;
                cancelled = 0;

                if (pick < threshold)
                {
                    delivered = 1;
                }
                else
                {
                    threshold += expiredWeight;
                    if (pick < threshold)
                    {
                        expired = 1;
                    }
                    else
                    {
                        threshold += undelivWeight;
                        if (pick < threshold)
                        {
                            undeliv = 1;
                        }
                        else
                        {
                            threshold += rejectdWeight;
                            if (pick < threshold)
                            {
                                rejectd = 1;
                            }
                            else
                            {
                                threshold += spamWeight;
                                if (pick < threshold)
                                {
                                    spam = 1;
                                }
                                else
                                {
                                    cancelled = 1;
                                }
                            }
                        }
                    }
                }
            }

            var optOuts = 0L;
            var replies = 0L;
            if (delivered > 0)
            {
                if (_random.NextDouble() < engagement.OptOutRate)
                {
                    optOuts = 1;
                }
                else if (_random.NextDouble() < engagement.ReplyRate)
                {
                    replies = 1;
                }
            }

            yield return new SmsEvent(subject.Id, carrier, messageType, timestamp, delivered, expired, undeliv, rejectd, spam, cancelled, replies, optOuts);
        }
    }

    private EngagementProfile GetEngagementProfile(string messageType, bool isAnomaly)
    {
        var (replyRate, optOutRate) = messageType switch
        {
            "OTP" => (0.12, 0.002),
            "Transactional" => (0.35, 0.003),
            "Promotional" => (0.10, 0.010),
            _ => (0.10, 0.010)
        };

        if (isAnomaly)
        {
            optOutRate += 0.020;
            replyRate = Math.Max(0.01, replyRate - 0.040);
        }

        replyRate += (_random.NextDouble() - 0.5) * 0.02;
        optOutRate += (_random.NextDouble() - 0.5) * 0.002;

        return new EngagementProfile(
            ReplyRate: Math.Clamp(replyRate, 0.0, 0.80),
            OptOutRate: Math.Clamp(optOutRate, 0.0, 0.10));
    }
}
