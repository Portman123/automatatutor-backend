﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using System.Diagnostics;

using Microsoft.Automata;
using Microsoft.Z3;

namespace AutomataPDL
{
    public static class DFAGrading
    {
        /// <summary>
        /// Computes the grade for attempt using all the possible metrics
        /// </summary>
        /// <param name="solution">correct dfa</param>
        /// <param name="attempt">dfa to be graded</param>
        /// <param name="alpahbet">input alphabet</param>
        /// <param name="solver">SMT solver for char set</param>
        /// <param name="timeout">timeout for the PDL enumeration (suggested > 1000)</param>
        /// <param name="maxGrade">Max grade for the homework</param>
        /// <returns>Grade for dfa2</returns>
        public static Pair<int, IEnumerable<DFAFeedback>> GetGrade(
            Automaton<BDD> solution, Automaton<BDD> attempt, HashSet<char> alpahbet,
            CharSetSolver solver, long timeout, int maxGrade)
        {            
            return GetGrade(solution, attempt, alpahbet, solver, timeout, maxGrade, FeedbackLevel.Minimal, true, true, true, false);
        }

        /// <summary>
        /// Computes the grade for attempt using all the possible metrics
        /// </summary>
        /// <param name="solution">correct dfa</param>
        /// <param name="attempt">dfa to be graded</param>
        /// <param name="alpahbet">input alphabet</param>
        /// <param name="solver">SMT solver for char set</param>
        /// <param name="timeout">timeout for the PDL enumeration (suggested > 1000)</param>
        /// <param name="maxGrade">Max grade for the homework</param>
        /// <param name="level">Feedback level</param>
        /// <returns>Grade for dfa2</returns>
        public static Pair<int, IEnumerable<DFAFeedback>> GetGrade(
            Automaton<BDD> solution, Automaton<BDD> attempt, HashSet<char> alpahbet,
            CharSetSolver solver, long timeout, int maxGrade, FeedbackLevel level)
        {
            return GetGrade(solution, attempt, alpahbet, solver, timeout, maxGrade, level, true, true, true, false); ;
        }

        /// <summary>
        /// Computes the grade for attempt using all the possible metrics
        /// </summary>
        /// <param name="dfaGoal">minimal correct dfa</param>
        /// <param name="dfaAttempt">dfa to be graded</param>
        /// <param name="al">input alphabet</param>
        /// <param name="solver">SMT solver for char set</param>
        /// <param name="timeout">timeout for the PDL enumeration (suggested > 1000)</param>
        /// <param name="maxGrade">Max grade for the homework</param>        
        /// <param name="enableDFAED">true to enable DFA edit distance</param> *Edit Distance: Quantifying how dissimilar two things are (Wikipedia)
        /// <param name="enablePDLED">true to enable PDL edit distance</param>
        /// <param name="enableDensity">true to enable density distance</param>
        /// <param name="isMinimiseProblem">true if need to grade a minimisation problem</param>
        /// <returns>Grade for dfa2</returns>
        public static Pair<int, IEnumerable<DFAFeedback>> GetGrade(
            Automaton<BDD> dfaGoal, Automaton<BDD> dfaAttempt, HashSet<char> al,
            CharSetSolver solver, long timeout,
            int maxGrade, FeedbackLevel level,
            bool enableDFAED, bool enablePDLED, bool enableDensity,
            bool isMinimiseProblem)
        {
            PDLEnumerator pdlEnumerator = new PDLEnumerator();

            var feedList = new List<DFAFeedback>();

            DFAFeedback defaultFeedback = new StringFeedback(level, StringFeedbackType.Wrong, al, solver);

            #region Accessory and initial vars
            // By default student's attempt is not minimised 
            var dfaAttemptMin = dfaAttempt;
            if (!isMinimiseProblem)
            {   // if this is not grading for a minimisation problem, minimise it
                dfaAttemptMin = dfaAttemptMin.Determinize(solver).Minimize(solver);
            }
            //Compute minimized version of DFAs
            var dfaGoalMin = dfaGoal.Determinize(solver).Minimize(solver);
            

            //Initialize distances at high values in case they are not used
            // they only produce positive grade if between 0 and 1
            double pdlEditDistanceScaled = 2;
            double densityRatio = 2;
            double dfaED = 2;
            #endregion

            #region deductions on the grade based on the size of the dfa
            //Deduction if DFA is smaller than it should be: used only for PDL ed and for density
            var smallerDFADeduction = 0.2 * Math.Sqrt(
                        Math.Max(0.0, dfaGoalMin.StateCount - dfaAttemptMin.StateCount) /
                        ((double)dfaGoalMin.StateCount));
            #endregion

            #region check whether the attempt is equivalent to the solution as well as if the number of states is the same.
            // Other than minimisation problems, every dfa attempt will be minimised.
            // Therefore, correct dfa attempts will always have the same number of states as the minimised dfa goal
            // i.e. Assuming the Atuomaton Library minimsation algorithm works properly, 
            // there will never be an instance where the dfa attempt is equivalent to the dfa goal but with a differing number of states. (except when grading minimisation problems)
            if (dfaGoalMin.IsEquivalentWith(dfaAttemptMin, solver) && dfaGoalMin.StateCount == dfaAttemptMin.StateCount)
            {
                Console.WriteLine("Correct");
                feedList.Add(new StringFeedback(level, StringFeedbackType.Correct, al, solver));
                return new Pair<int, IEnumerable<DFAFeedback>>(maxGrade, feedList);
            } 
            // if this is a minimisation problem and the user attempt is accepts the same language but has not been minimised give meaninful feedback 
            else if (isMinimiseProblem && dfaGoalMin.IsEquivalentWith(dfaAttemptMin, solver) && !(dfaGoalMin.StateCount == dfaAttemptMin.StateCount))
            {
                Console.WriteLine("Not Minimised");
                feedList.Add(new StringFeedback(level, StringFeedbackType.Minimisation, al, solver));
                return new Pair<int, IEnumerable<DFAFeedback>>((int)Math.Round((double)maxGrade / 4), feedList);
            }
            #endregion

            #region metrics computation
            Stopwatch swPDLed = new Stopwatch();
            swPDLed.Start();
            #region PDL edit distance
            Transformation feedbackTransformation = null;
            if (enablePDLED)
            {
                var trpair = PDLEditDistance.GetMinimalFormulaEditDistanceTransformation(dfaGoalMin, dfaAttemptMin, al, solver, timeout, pdlEnumerator);

                if (trpair != null)
                {
                    var transformationGrade = trpair.First;
                    feedbackTransformation = trpair.Second;
                    var scaling = 1.0;
                    pdlEditDistanceScaled = transformationGrade.totalCost / (transformationGrade.minSizeForTreeA * scaling) + smallerDFADeduction;
                }
            }
            #endregion
            swPDLed.Stop();

            Stopwatch swDensity = new Stopwatch();
            swDensity.Start();
            #region density distance
            if (enableDensity)
            {
                densityRatio = DFADensity.GetDFADifferenceRatio(dfaGoalMin, dfaAttemptMin, al, solver);
                densityRatio += smallerDFADeduction;
            }
            #endregion
            swDensity.Stop();

            Stopwatch swDFAed = new Stopwatch();
            swDFAed.Start();
            #region DFA edit distance
            DFAEditScript dfaEditScript = null;
            if (enableDFAED)
            {
                //limit the depth of the DFA edit distance search
                var maxMoves = Math.Max(1, 6 - (int)Math.Sqrt(dfaAttempt.MoveCount + dfaAttempt.StateCount));
                dfaEditScript = DFAEditDistance.GetDFAOptimalEdit(dfaGoal, dfaAttempt, al, solver, timeout, new StringBuilder());

                if (dfaEditScript != null)
                    dfaED = ((double)(dfaEditScript.GetCost())) / ((double)((dfaGoalMin.StateCount + 1) * al.Count));

            }
            #endregion
            swDFAed.Stop();

            #endregion

            #region metrics scaling
            var scalingSquarePDLED = 1.005;
            var scalingSquareDensity = 1; var multv2 = 0.5;
            var scalingSquareDFAED = 1.03;

            var scaledPdlED = (0.9 * (scalingSquarePDLED + pdlEditDistanceScaled) * (scalingSquarePDLED + pdlEditDistanceScaled)) - scalingSquarePDLED * scalingSquarePDLED;
            var scaledDensityRatio = (scalingSquareDensity + (multv2 * densityRatio)) * (scalingSquareDensity + (multv2 * densityRatio)) - scalingSquareDensity * scalingSquareDensity;
            var scaledDfaED = (scalingSquareDFAED + dfaED) * (scalingSquareDFAED + dfaED) - scalingSquareDFAED * scalingSquareDFAED;


            //Select dominating Feedback based on grade
            double unscaledGrade = Math.Min(Math.Min(scaledPdlED, scaledDensityRatio), scaledDfaED);
            var pdledwins = scaledPdlED <= Math.Min(scaledDensityRatio, scaledDfaED);
            var dfaedwins = scaledDfaED <= Math.Min(scaledDensityRatio, scaledPdlED);
            var densitywins = scaledDensityRatio <= Math.Min(scaledDfaED, scaledPdlED);
            #endregion

            #region Feedback Selection
            if (pdledwins && feedbackTransformation != null && feedbackTransformation.pdlB.GetFormulaSize()<10)
                feedList.Add(new PDLEDFeedback(level, al, feedbackTransformation, scaledPdlED, solver));

            if ((dfaedwins || feedList.Count == 0) && dfaEditScript != null && !dfaEditScript.IsComplex())
            {
                feedList = new List<DFAFeedback>();
                feedList.Add(new DFAEDFeedback(dfaGoal, dfaAttempt, level, al, dfaEditScript, scaledDfaED, solver));
            }

            if (densitywins || feedList.Count == 0)
            {
                feedList = new List<DFAFeedback>();
                feedList.Add(new DensityFeedback(level, al, dfaGoal, dfaAttempt, scaledDensityRatio, solver));
            }

            if (feedList.Count == 0)
            {
                Console.WriteLine("Why no feedback!!");
                feedList.Add(defaultFeedback);
            }
            #endregion           

            #region normalize grade
            var scaledGrade = maxGrade - (int)Math.Round(unscaledGrade * (double)(maxGrade));
            //If rounding yields maxgrade deduct 1 point by default
            if (scaledGrade == maxGrade)
                scaledGrade = maxGrade - 1;

            //Remove possible deduction
            scaledGrade = scaledGrade < 0 ? 0 : scaledGrade;
            return new Pair<int, IEnumerable<DFAFeedback>>(scaledGrade, feedList);
            #endregion
        }

        #region Methods for checking DFA well-formedness
        /// <summary>
        /// Returns true iff the DFA is not complete or not a DFA (misses transitions)
        /// </summary>
        /// <param name="dfa"></param>
        /// <param name="al"></param>
        /// <param name="solver"></param>
        /// <returns></returns>
        public static bool ContainsSyntacticMistake(Automaton<BDD> dfa, HashSet<char> al,
            CharSetSolver solver, HashSet<int> missingEdges)
        {
            bool mistake = false;
            var dfaNorm = DFAUtilities.normalizeDFA(dfa).First;
            foreach (var state in dfaNorm.States)
            {
                HashSet<char> alCopy = new HashSet<char>(al);
                foreach (var move in dfaNorm.GetMovesFrom(state))
                {
                    foreach (var c in solver.GenerateAllCharacters(move.Label, false))
                    {
                        if (!alCopy.Contains(c))
                        {
                            int hash = (int)(Math.Pow(2, move.SourceState) + Math.Pow(3, c - 97)) + dfaNorm.StateCount;
                            mistake = true;
                        }
                        alCopy.Remove(c);
                    }
                }
                if (alCopy.Count > 0)
                    mistake=true;
            }
            return mistake;
        }
        #endregion
    }

}
