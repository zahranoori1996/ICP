namespace Application.DTOs;

/// <summary>
/// Represents a request to perform blank and scale optimization on project data.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to optimize.</param>
/// <param name="Elements">An optional list of element symbols to optimize. If null, all valid elements are processed.</param>
/// <param name="MinDiffPercent">The minimum difference percentage allowed for a sample to pass. Defaults to -10%.</param>
/// <param name="MaxDiffPercent">The maximum difference percentage allowed for a sample to pass. Defaults to 10%.</param>
/// <param name="MaxIterations">The maximum number of iterations the optimization algorithm should perform.</param>
/// <param name="PopulationSize">The size of the candidate population for the evolutionary algorithm.</param>
/// <param name="UseMultiModel">Indicates whether to evaluate multiple models (e.g., A, B, C) and select the best one.</param>
/// <param name="Seed">An optional integer seed to ensure reproducible random number generation.</param>
public record BlankScaleOptimizationRequest(
    Guid ProjectId,
    List<string>? Elements = null,
    decimal MinDiffPercent = -10m,
    decimal MaxDiffPercent = 10m,
    int MaxIterations = 100,
    int PopulationSize = 20,
    bool UseMultiModel = true,
    int? Seed = null
);

/// <summary>
/// Contains the complete results of a blank and scale optimization process.
/// </summary>
/// <param name="TotalSamples">The total number of samples included in the analysis.</param>
/// <param name="PassedBefore">The number of samples that met the passing criteria before optimization.</param>
/// <param name="PassedAfter">The number of samples that met the passing criteria after optimization.</param>
/// <param name="ImprovementPercent">The percentage increase in passing samples achieved by the optimization.</param>
/// <param name="ElementOptimizations">A dictionary of detailed optimization results for each element.</param>
/// <param name="OptimizedData">The list of sample data updated with the optimized values.</param>
/// <param name="ModelSummary">An optional summary object detailing the performance of multiple models if used.</param>
public record BlankScaleOptimizationResult(
    int TotalSamples,
    int PassedBefore,
    int PassedAfter,
    decimal ImprovementPercent,
    Dictionary<string, ElementOptimization> ElementOptimizations,
    List<OptimizedSampleDto> OptimizedData,
    MultiModelSummary? ModelSummary = null
);

/// <summary>
/// Details the optimization parameters and results for a single chemical element.
/// </summary>
/// <param name="Element">The chemical symbol of the element.</param>
/// <param name="OptimalBlank">The calculated blank value that yields the best results.</param>
/// <param name="OptimalScale">The calculated scale factor that yields the best results.</param>
/// <param name="PassedBefore">The number of samples passing for this element before optimization.</param>
/// <param name="PassedAfter">The number of samples passing for this element after optimization.</param>
/// <param name="MeanDiffBefore">The average difference from expected values before optimization.</param>
/// <param name="MeanDiffAfter">The average difference from expected values after optimization.</param>
/// <param name="SelectedModel">The identifier of the math model used for this element (e.g., "A").</param>
public record ElementOptimization(
    string Element,
    decimal OptimalBlank,
    decimal OptimalScale,
    int PassedBefore,
    int PassedAfter,
    decimal MeanDiffBefore,
    decimal MeanDiffAfter,
    string SelectedModel = "A"
);

/// <summary>
/// Represents a sample's data state, including values before and after optimization.
/// </summary>
/// <param name="SolutionLabel">The unique label of the sample.</param>
/// <param name="CrmId">The identifier of the CRM associated with this sample.</param>
/// <param name="OriginalValues">Accurate uncorrected values originally measured.</param>
/// <param name="CrmValues">The certified reference values for the CRM.</param>
/// <param name="OptimizedValues">The values after applying the optimal blank and scale.</param>
/// <param name="DiffPercentBefore">Percentage differences from CRM values before optimization.</param>
/// <param name="DiffPercentAfter">Percentage differences from CRM values after optimization.</param>
/// <param name="PassStatusBefore">Boolean map indicating which elements passed criteria before optimization.</param>
/// <param name="PassStatusAfter">Boolean map indicating which elements passed criteria after optimization.</param>
public record OptimizedSampleDto(
    string SolutionLabel,
    string CrmId,
    Dictionary<string, decimal?> OriginalValues,
    Dictionary<string, decimal?> CrmValues,
    Dictionary<string, decimal?> OptimizedValues,
    Dictionary<string, decimal> DiffPercentBefore,
    Dictionary<string, decimal> DiffPercentAfter,
    Dictionary<string, bool> PassStatusBefore,
    Dictionary<string, bool> PassStatusAfter
);

/// <summary>
/// Represents a request to manually apply specific blank and scale values to an element.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project.</param>
/// <param name="Element">The chemical symbol of the element to adjust.</param>
/// <param name="Blank">The specific blank value to apply.</param>
/// <param name="Scale">The specific scale factor to apply.</param>
public record ManualBlankScaleRequest(
    Guid ProjectId,
    string Element,
    decimal Blank,
    decimal Scale
);

/// <summary>
/// Contains the result of a manual blank and scale adjustment operation.
/// </summary>
/// <param name="Element">The chemical symbol of the element corrected.</param>
/// <param name="Blank">The blank value that was applied.</param>
/// <param name="Scale">The scale factor that was applied.</param>
/// <param name="PassedBefore">Pass count derived from the previous state.</param>
/// <param name="PassedAfter">Pass count resulting from the new manual settings.</param>
/// <param name="OptimizedData">The sample data re-calculated with the manual settings.</param>
public record ManualBlankScaleResult(
    string Element,
    decimal Blank,
    decimal Scale,
    int PassedBefore,
    int PassedAfter,
    List<OptimizedSampleDto> OptimizedData
);

/// <summary>
/// Summarizes the performance and selection distribution of multiple optimization models.
/// </summary>
/// <param name="ElementsOptimizedWithModelA">The count of elements for which Model A was determined to be best.</param>
/// <param name="ElementsOptimizedWithModelB">The count of elements for which Model B was determined to be best.</param>
/// <param name="ElementsOptimizedWithModelC">The count of elements for which Model C was determined to be best.</param>
/// <param name="MostUsedModel">The identifier of the model that was selected for the majority of elements.</param>
/// <param name="Summary">A textual overview of the multi-model optimization outcomes.</param>
public record MultiModelSummary(
    int ElementsOptimizedWithModelA,
    int ElementsOptimizedWithModelB,
    int ElementsOptimizedWithModelC,
    string MostUsedModel,
    string Summary
);

/// <summary>
/// Encapsulates the results of running multiple optimization models for comparison.
/// </summary>
/// <param name="BestModel">The identifier of the model selected as the best performer.</param>
/// <param name="SelectionReason">A description of why the best model was chosen.</param>
/// <param name="ModelA">The detailed results produced by Model A.</param>
/// <param name="ModelB">The detailed results produced by Model B.</param>
/// <param name="ModelC">The detailed results produced by Model C.</param>
public record MultiModelOptimizationResult(
    string BestModel,
    string SelectionReason,
    ModelResult ModelA,
    ModelResult ModelB,
    ModelResult ModelC
);

/// <summary>
/// Represents the execution result of a single optimization model strategy.
/// </summary>
/// <param name="ModelName">The name or identifier of the model.</param>
/// <param name="Description">A brief description of how the model operates.</param>
/// <param name="Success">True if the model successfully converged or completed; otherwise, false.</param>
/// <param name="PassedCount">The number of samples that met criteria using this model.</param>
/// <param name="TotalDistance">The total calculated distance metric (fitness) for this model.</param>
/// <param name="TotalSSE">The total sum of squared errors associated with this model's solution.</param>
/// <param name="Optimizations">The per-element optimization parameters derived by this model.</param>
/// <param name="ErrorMessage">An error message if the model execution failed.</param>
public record ModelResult(
    string ModelName,
    string Description,
    bool Success,
    int PassedCount,
    double TotalDistance,
    double TotalSSE,
    Dictionary<string, ElementOptimization> Optimizations,
    string? ErrorMessage = null
);

/// <summary>
/// details the comparison between different models for a specific single element.
/// </summary>
/// <param name="Element">The chemical symbol of the element.</param>
/// <param name="ModelA">The performance metrics for Model A.</param>
/// <param name="ModelB">The performance metrics for Model B.</param>
/// <param name="ModelC">The performance metrics for Model C.</param>
/// <param name="SelectedModel">The model identifier that was selected as superior.</param>
/// <param name="SelectionReason">The rationale for the model selection.</param>
public record ElementModelComparison(
    string Element,
    ElementModelResult ModelA,
    ElementModelResult ModelB,
    ElementModelResult ModelC,
    string SelectedModel,
    string SelectionReason
);

/// <summary>
/// Contains the specific performance metrics for a model applied to one element.
/// </summary>
/// <param name="Blank">The blank value proposed by the model.</param>
/// <param name="Scale">The scale factor proposed by the model.</param>
/// <param name="PassedCount">The number of samples passing criteria with these settings.</param>
/// <param name="HuberDistance">The calculated Huber distance indicating fit quality.</param>
/// <param name="SSE">The sum of squared errors indicating fit quality.</param>
/// <param name="MeanDiffPercent">The average percentage difference from expected values.</param>
public record ElementModelResult(
    decimal Blank,
    decimal Scale,
    int PassedCount,
    double HuberDistance,
    double SSE,
    decimal MeanDiffPercent
);