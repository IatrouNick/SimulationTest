﻿using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the autograder-related aspects of an autograder level and run.
/// </summary>
public class AutograderManager : MonoBehaviour
{
    #region Constants
    /// <summary>
    /// The build index of the level which displays a summary of an autograder run.
    /// </summary>
    public const int AutograderSummaryBuildIndex = 25;
    #endregion

    #region Public Interface
    /// <summary>
    /// The score for each completed level in the current autograder run.
    /// </summary>
    public static readonly List<AutograderLevelScore> levelScores = new List<AutograderLevelScore>();

    /// <summary>
    /// Autograder information about the current level.
    /// </summary>
    public static AutograderLevelInfo LevelInfo { get { return LevelManager.LevelInfo.AutograderLevels[AutograderManager.levelIndex]; } }

    /// <summary>
    /// Reset the autograder for a new lab.
    /// </summary>
    public static void ResetAutograder()
    {
        AutograderManager.levelIndex = 0;
        AutograderManager.levelScores.Clear();
    }

    /// <summary>
    /// Register when a task is successfully completed.
    /// </summary>
    /// <param name="task">The task which was completed.</param>
    public static void CompleteTask(AutograderTask task)
    {
        if (AutograderManager.CurTask == task)
        {
            task.Disable();
            AutograderManager.instance.levelScore += task.Points;
            AutograderManager.instance.hud.UpdateScore(AutograderManager.instance.levelScore, AutograderManager.LevelInfo.MaxPoints);
            AutograderManager.instance.taskIndex++;
            if (AutograderManager.instance.taskIndex >= AutograderManager.instance.tasks.Length)
            {
                if (!AutograderManager.LevelInfo.DoNotProceedUntilStopped)
                {
                    AutograderManager.instance.FinishLevel();
                }
            }
            else
            {
                AutograderManager.CurTask.Enable();
            }
        }
        else
        {
            Debug.LogError($"[AutograderManager::CompleteTask]: CompleteTask was called for task [{task}], but this is not the active task. No action taken.");
        }
    }

    /// <summary>
    /// Begin the autograder for a particular level.
    /// </summary>
    /// <param name="hud">The autograder HUD used for this level.</param>
    public void HandleStart(IAutograderHud hud)
    {
        this.startTime = Time.time;
        this.hud = hud;
        this.hud.SetLevelInfo(AutograderManager.levelIndex, AutograderManager.LevelInfo.Title, AutograderManager.LevelInfo.Description);
        this.hud.UpdateScore(this.levelScore, AutograderManager.LevelInfo.MaxPoints);
        this.hud.UpdateTime(0, AutograderManager.LevelInfo.TimeLimit);

        if (AutograderManager.LevelInfo.TimeBonuses != null)
        {
            Vector2 timeBonus = AutograderManager.LevelInfo.TimeBonuses[this.timeBonusIndex];
            this.hud.SetTimeBonus(timeBonus.x, timeBonus.y, false);
        }
        else
        {
            this.hud.SetMaxTime(AutograderManager.LevelInfo.TimeLimit);
        }
    }

    /// <summary>
    /// Handles when an error occurs.
    /// </summary>
    public void HandleError()
    {
        AutograderManager.levelScores.Add(new AutograderLevelScore()
        {
            Score = this.levelScore,
            Time = Time.time - this.startTime ?? Time.time
        });
        AutograderSummary.WasError = true;
    }

    /// <summary>
    /// Handles when the user fails the current autograder level.
    /// </summary>
    public void HandleFailure()
    {
        this.FinishLevel();
    }
    #endregion

    /// <summary>
    /// A static reference to the current LevelManager (there is only ever one at a time).
    /// </summary>
    private static AutograderManager instance;

    /// <summary>
    /// The index of the current autograder level in the overall autograder run.
    /// </summary>
    private static int levelIndex = 0;

    /// <summary>
    /// The tasks which must be completed for the autograder level, in order.
    /// </summary>
    private AutograderTask[] tasks = new AutograderTask[1];

    /// <summary>
    /// The HUD which displays autograder information.
    /// </summary>
    private IAutograderHud hud;

    /// <summary>
    /// The Time.time at which the autograder started for this level.
    /// </summary>
    private float? startTime = null;

    /// <summary>
    /// The index of the task which must be completed.
    /// </summary>
    private int taskIndex = 0;

    /// <summary>
    /// The total number of points which the user has earned on the current level.
    /// </summary>
    private float levelScore = 0;

    /// <summary>
    /// True when FinishLevel() was called for the current level.
    /// </summary>
    private bool wasFinishedCalled = false;

    /// <summary>
    /// The index of the current time bonus in LevelInfo.TimeBonuses.
    /// </summary>
    private int timeBonusIndex = 0;

    /// <summary>
    /// The current task which must be completed.
    /// </summary>
    private static AutograderTask CurTask { get { return AutograderManager.instance.tasks[AutograderManager.instance.taskIndex]; } }

    private void Awake()
    {
        AutograderManager.instance = this;
        this.tasks = this.GetComponentsInChildrenOrdered<AutograderTask>();
    }

    private void Start()
    {
        AutograderManager.CurTask.Enable();
    }

    private void Update()
    {
        if (this.startTime.HasValue && !this.wasFinishedCalled)
        {
            float elapsedTime = Time.time - this.startTime.Value + LevelManager.TimePenalty;
            this.hud.UpdateTime(elapsedTime, AutograderManager.LevelInfo.TimeLimit);

            Vector2[] timeBonuses = AutograderManager.LevelInfo.TimeBonuses;
            if (timeBonuses != null && elapsedTime > timeBonuses[this.timeBonusIndex].x)
            {
                this.timeBonusIndex++;
                Vector2 timeBonus = timeBonuses[this.timeBonusIndex];

                // If this is the last time bonus, use the overall level time limit
                if (timeBonus.x == float.PositiveInfinity)
                {
                    this.hud.SetTimeBonus(AutograderManager.LevelInfo.TimeLimit, timeBonus.y, true);
                }
                else
                {
                    this.hud.SetTimeBonus(timeBonus.x, timeBonus.y, false);
                }
            }

            if (elapsedTime > AutograderManager.LevelInfo.TimeLimit ||
                (AutograderManager.LevelInfo.DoNotProceedUntilStopped && this.taskIndex >= this.tasks.Length && LevelManager.GetCar().Physics.LinearVelocity.magnitude < Constants.MaxStopSeed) ||
                Input.GetKeyDown(KeyCode.Tab))
            {
                this.FinishLevel();
            }
        }
    }

    /// <summary>
    /// Handles when the current level is finished, whether successfully or not.
    /// </summary>
    private void FinishLevel()
    {
        // This check is necessary to prevent FinishLevel from being called twice, since it may take multiple frames to load the next level.
        if (!this.wasFinishedCalled)
        {
            this.wasFinishedCalled = true;

            // Apply time bonus if all tasks were completed
            if (this.taskIndex >= this.tasks.Length && AutograderManager.LevelInfo.TimeBonuses != null)
            {
                this.levelScore += AutograderManager.LevelInfo.TimeBonuses[this.timeBonusIndex].y;
            }

            AutograderManager.levelScores.Add(new AutograderLevelScore()
            {
                Score = this.levelScore,
                Time = Time.time - this.startTime + LevelManager.TimePenalty ?? Time.time
            });

            if (AutograderManager.levelIndex == LevelManager.LevelInfo.AutograderLevels.Length - 1)
            {
                LevelManager.FinishAutograder();
            }
            else if (AutograderManager.LevelInfo.IsRequired && this.levelScore < AutograderManager.LevelInfo.MaxPoints)
            {
                AutograderSummary.WasRequiredLevelFailed = true;
                LevelManager.FinishAutograder();
            }
            else
            {
                AutograderManager.levelIndex++;
                LevelManager.NextAutograderLevel();
            }
        }
        else
        {
            Debug.LogError($"[AutograderManager::FinishLevel] Attempted to call FinishLevel twice for level [{AutograderManager.LevelInfo.Title}], second call ignored.");
        }
    }
}
