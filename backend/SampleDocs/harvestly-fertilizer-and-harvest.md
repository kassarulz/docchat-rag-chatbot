# Harvestly – Fertilizer, Mixes and Harvest Records

This guide explains how Harvestly tracks fertilizer stock, daily nutrient mixes and harvests, and how those records feed into crop costs.

## Fertilizer stock

Each fertilizer product has a name, a unit (g, kg, ml or l), a unit price and a low-stock threshold. When stock falls below the threshold, the product appears in the dashboard's low-stock list and managers receive a daily email reminder at 7:00 AM local time.

To add stock, open the product and choose **Add purchase**. Enter the quantity, the total price and the supplier. Harvestly recalculates the product's average unit price using a weighted average of all purchases, so older cheaper stock and newer expensive stock are blended fairly.

Stock can never go below zero. If a mix would use more than the available quantity, Harvestly blocks the mix and shows how much is missing.

## Daily fertilizer mixes

A **mix** records the nutrients prepared for one or more tunnels on a given day. For each mix you enter:

1. The date and the tunnels it was applied to.
2. Each product and the quantity used.
3. The water volume in litres.

When a mix is saved, the used quantities are deducted from stock immediately. If a mix is applied to several tunnels, its cost is split between them in proportion to their floor area. For example, a mix applied to a 300 m² tunnel and a 100 m² tunnel charges 75% of the cost to the first and 25% to the second.

Workers can edit a mix for 24 hours after creating it. After that, only managers and admins can edit it, and every change is written to the audit log.

## Recommended EC and pH ranges

Harvestly shows a warning when a mix's recorded EC or pH falls outside the recommended range for the crop:

- **Tomato:** EC 2.0–3.5 mS/cm, pH 5.8–6.5
- **Bell pepper:** EC 1.8–2.8 mS/cm, pH 5.8–6.3
- **Cucumber:** EC 1.7–2.5 mS/cm, pH 5.5–6.0

These are general guidance values. Growers should adjust them for their variety, growth stage and water source.

## Harvest records

Harvests are recorded per crop. Each record has a date, a weight in kilograms and a grade: **A** (export quality), **B** (local market) or **C** (processing or rejected). Workers can add harvest records from their phone.

The crop's yield report shows total weight by grade and the yield per plant. Harvestly calculates **cost per kilogram** by dividing the crop's total recorded cost (fertilizer share, labour and other expenses) by the total harvested weight of grades A and B. Grade C weight is excluded because it is usually sold at a loss.

## Sales

If you record sales against a harvest, Harvestly shows gross margin per crop: sales revenue minus total crop cost. Sales are optional; many growers only use cost tracking.
