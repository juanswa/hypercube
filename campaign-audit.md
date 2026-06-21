# 📊 Executive Campaign Performance Audit

## 🗣️ Executive narrative
- SMS send report for sender-demo: 495 observations, 448 material. 28 self-anomalies, 13 peer deviations detected. Overall, 2,000,000 messages were sent, 1,989,970 were attempted at carriers, and 166,079 were not delivered (8.3 % failure over attempted traffic). Of 166,079 failures: REJECTD 21.8 %, SPAM 6.0 %, UNDELIV 12.0 %, EXPIRED 60.2 %. Dominant reason: EXPIRED. Failure concentration is meaningful: mtn|promotional contributes 17.2 % of failed attempted messages. Hour-of-day spread is 0.6 % between the best and worst hours.

## 👑 Campaign totals
- **Campaign Score / Grade:** **C**
- **Sent:** 2,000,000
- **Attempted:** 1,989,970
- **Delivered:** 1,823,891
- **Failed:** 166,079
- **Cancelled:** 10,030
- **Delivery rate:** 91.7 %
- **Failure rate:** 8.3 %
- **Rate meaning:** Delivery and failure rates use **Attempted = Sent − Cancelled** as the denominator.
- **Window:** 2026-06-21 14:49:40Z → 2026-06-21 14:49:54Z; **#segments:** 12
- **Mode:** Local AI (ONNX Runtime - Fully Offline)
- **Subject:** sender-demo; **Total messages:** 2,000,000

## 🧭 Segment leaderboard
- **Worst:** mtn|promotional · failure 17.2 % · sent 166,563 · 2.1× campaign average
- **Worst:** cellc|transactional · failure 7.7 % · sent 166,642 · 0.9× campaign average
- **Worst:** telkom|transactional · failure 7.6 % · sent 166,938 · 0.9× campaign average
- **Best:** cellc|promotional · delivery 92.6 % · sent 166,574 · 0.9× campaign avg failure
- **Best:** mtn|transactional · delivery 92.5 % · sent 167,032 · 0.9× campaign avg failure
- **Best:** telkom|otp · delivery 92.5 % · sent 167,045 · 0.9× campaign avg failure

## 🧪 Status mix
- DELIVRD: 1,823,891 (91.7 % of attempted)
- EXPIRED: 99,955 (5.0 % of attempted)
- UNDELIV: 19,940 (1.0 % of attempted)
- REJECTD: 36,182 (1.8 % of attempted)
- SPAM: 10,002 (0.5 % of attempted)
- CANCELLED: 10,030 (0.5 % of sent)

## 🔀 Delivery flow (Sankey)
```text
SENT 2,000,000
  ├─ ATTEMPTED 1,989,970 (99.5 % of sent)
  └─ CANCELLED 10,030 (0.5 % of sent)
      = Balance check over sent denominator: ATTEMPTED + CANCELLED = SENT (2,000,000 = 2,000,000, 100.0% of sent)
  │   ├─ DELIVRD 1,823,891 (91.7 % of attempted)
  │   └─ FAILED 166,079 (8.3 % of attempted)
  │       = Balance check over attempted denominator: DELIVRD + FAILED = ATTEMPTED (1,989,970 = 1,989,970, 100.0% of attempted)
  │       ├─ EXPIRED 99,955 (60.2 % of failed)
  │       ├─ UNDELIV 19,940 (12.0 % of failed)
  │       ├─ REJECTD 36,182 (21.8 % of failed)
  │       └─ SPAM 10,002 (6.0 % of failed)
  │           = Balance check over failed denominator: EXPIRED + UNDELIV + REJECTD + SPAM = FAILED (166,079 = 166,079, 100.0% of failed)
```

## 🧩 Failure-reason ranking
- Of 166,079 failures: REJECTD 21.8 %, SPAM 6.0 %, UNDELIV 12.0 %, EXPIRED 60.2 %. Dominant reason: EXPIRED.

## 🎯 Worst segment per reason
- REJECTD: mtn|promotional · rate 10.7 % · sent 166,563
- SPAM: mtn|transactional · rate 0.5 % · sent 167,032
- UNDELIV+EXPIRED: telkom|transactional · rate 6.1 % · sent 166,938

## 📉 Failure concentration (Pareto)
- 17.2 % of all failures came from mtn|promotional; 62.4 % of those were REJECTD — a likely carrier-filter/throttle signature.

## ⏰ Timing patterns
- Delivery dipped to 91.3 % at hour-of-day 12, vs 91.9 % at peak (spread 0.6 %); weekday 92.2 %, weekend 90.4 %.

## ⚠️ Ranked anomaly red flags
- **SelfAnomaly:** Carrier × message type (mtn|promotional) failure_rate; actual 17.2 %, baseline 3.0 %, deviation +14.2%, sent 166,563.
- **BelowPeers:** Carrier × message type (mtn|promotional) delivery_rate; actual 82.8 %, baseline 97.0 %, deviation -14.2%, sent 166,563.
- **SelfAnomaly:** Carrier × message type (mtn|promotional) rejectd_rate; actual 10.7 %, baseline 1.0 %, deviation +9.7%, sent 166,563.
- **BelowPeers:** Carrier (mtn) delivery_rate; actual 89.3 %, baseline 97.0 %, deviation -7.7%, sent 500,267.
- **SelfAnomaly:** Carrier (mtn) failure_rate; actual 10.7 %, baseline 3.0 %, deviation +7.7%, sent 500,267.
- **SelfAnomaly:** Message type (promotional) failure_rate; actual 9.9 %, baseline 3.0 %, deviation +6.9%, sent 666,467.
- **BelowPeers:** Message type (promotional) delivery_rate; actual 90.1 %, baseline 97.0 %, deviation -6.9%, sent 666,467.
- **BelowPeers:** Message type (transactional) delivery_rate; actual 92.4 %, baseline 97.0 %, deviation -4.6%, sent 666,949.

## 💥 Quantified impact
- **166,079 messages were not delivered.**
- 17.2 % of all failures came from mtn|promotional; 62.4 % of those were REJECTD — a likely carrier-filter/throttle signature.

## 🚀 Actionable recommendations
1. **Validity-window lapses (handset off / out of coverage). Extend validity or retry, and prefer higher-reachability send hours for mtn|promotional.**
2. **Secondary focus:** Prioritize mtn|promotional where failure is 2.1× campaign average, then work through ranked anomalies by severity × volume.

## Honest gaps
- Delivery latency: insufficient data; the v1 SMS schema does not include latency metrics.
- Sub-account/route-level attribution: insufficient data; the current schema does not include route identifiers.
