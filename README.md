# Master Thesis: Trajectory-Aware DDA Subsystem

This repository contains the source code for the artificial intelligence subsystem and the statistical evaluation pipeline developed for my Master's thesis: 
**"Trajectory-Aware Online Skill Estimation and Adaptive Difficulty Control in a Local Multiplayer Party Racing Game"** (Botond Kovács, FH Technikum Wien, 2026).

## Scope of this Repository

The base game, *Drive Me Crazy*, is a comprehensive Unity project containing extensive 3D assets, physics simulations, and core gameplay mechanics developed as a group project. 

To facilitate the grading process and explicitly isolate my individual scientific contribution, this repository does not include the heavy Unity project files or the base game logic. Instead, it exclusively contains the core C# scripts for the Dynamic Difficulty Adjustment (DDA) system and the Python script used for the empirical evaluation.

## Repository Structure

* **/Unity_DDA_System/**
  Contains the production-ready C# scripts integrated into the Unity runtime.
  * `SkillEstimator.cs`: The trajectory-aware online skill estimator using discrete and continuous EMA filters.
  * `DifficultyDirector.cs`: The multi-channel adaptive difficulty controller and driver-tenure rubberband logic.
  * `*TelemetryLogger.cs`: Custom telemetry pipeline scripts responsible for capturing discrete events and continuous trajectory data at 10 Hz.
  * `CsvLogWriter.cs` & `TelemetrySessionContext.cs`: Robust local CSV logging architecture for post-hoc analysis.

* **/Python_Evaluation/**
  Contains the data analysis pipeline used to evaluate the telemetry logs.
  * Includes the script used to compute Spearman's rank correlation coefficients, Mann-Whitney U tests for final score spread variance, and to generate spatial heatmaps for overspeed ratios and crash densities.