// RuleEngine.cs
// Evaluates all active rules against current context

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.RiskManager
{
    /// <summary>
    /// Central rule evaluation engine.
    /// Runs all enabled rules and collects violations.
    /// </summary>
    public class RuleEngine
    {
        private readonly List<RiskRule> _rules = new List<RiskRule>();
        private readonly object _lock = new object();

        public IReadOnlyList<RiskRule> Rules => _rules.AsReadOnly();

        public void AddRule(RiskRule rule)
        {
            lock (_lock)
            {
                _rules.Add(rule);
            }
        }

        public void RemoveRule(RiskRule rule)
        {
            lock (_lock)
            {
                _rules.Remove(rule);
            }
        }

        public void ClearRules()
        {
            lock (_lock)
            {
                _rules.Clear();
            }
        }

        /// <summary>
        /// Evaluate all enabled rules against the current context.
        /// Returns result with any violations and required action.
        /// </summary>
        public RuleResult Evaluate(RiskContext context)
        {
            var result = new RuleResult();

            lock (_lock)
            {
                foreach (var rule in _rules.Where(r => r.Enabled))
                {
                    try
                    {
                        if (rule.IsViolated(context))
                        {
                            result.Violations.Add(new RuleViolation
                            {
                                Rule = rule,
                                Action = rule.Action,
                                Message = rule.GetViolationMessage(context),
                                Timestamp = DateTime.Now,
                                Instrument = context.ViolatingInstrument
                            });

                            // Capture violating instrument for per-position rules
                            if (!string.IsNullOrEmpty(context.ViolatingInstrument))
                            {
                                result.ViolatingInstrument = context.ViolatingInstrument;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't crash on rule errors
                        NinjaTrader.Code.Output.Process(
                            $"[RiskManager] Rule '{rule.Name}' error: {ex.Message}",
                            NinjaTrader.NinjaScript.PrintTo.OutputTab1);
                    }
                }
            }

            // Determine highest severity action needed
            if (result.Violations.Any())
            {
                result.RequiredAction = result.Violations
                    .Select(v => v.Action)
                    .Max();
            }

            return result;
        }
    }

    /// <summary>
    /// Result of rule evaluation
    /// </summary>
    public class RuleResult
    {
        public List<RuleViolation> Violations { get; } = new List<RuleViolation>();
        public RuleAction RequiredAction { get; set; } = RuleAction.None;
        public bool HasViolations => Violations.Count > 0;

        // For per-position rules - which instrument violated
        public string ViolatingInstrument { get; set; }
    }

    /// <summary>
    /// Details of a single rule violation
    /// </summary>
    public class RuleViolation
    {
        public RiskRule Rule { get; set; }
        public RuleAction Action { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string Instrument { get; set; } // For per-position rules
    }
}
