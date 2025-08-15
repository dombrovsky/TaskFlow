namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a named configuration contract for providing <see cref="TaskFlowOptions"/> specific to named task flow instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="INamedConfigureTaskFlowOptions"/> interface enables named configuration of task flow options
    /// within the dependency injection system. This allows different task flow instances to have different
    /// configuration settings based on their intended purpose or usage context.
    /// </para>
    /// <para>
    /// Named options configuration provides:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Configuration separation</strong> - Different options for different named instances</item>
    ///   <item><strong>Runtime configuration</strong> - Options can be computed or resolved at runtime</item>
    ///   <item><strong>Dependency injection integration</strong> - Can access other services for configuration</item>
    ///   <item><strong>Environment-specific settings</strong> - Different options for different environments</item>
    /// </list>
    /// <para>
    /// The options configuration system works in conjunction with:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="INamedTaskFlowFactory"/> - For custom task flow creation</item>
    ///   <item><see cref="INamedConfigureTaskFlowChain"/> - For scheduler chain configuration</item>
    ///   <item><see cref="IDefaultTaskFlowFactory"/> - As the fallback when no named factory exists</item>
    /// </list>
    /// <para>
    /// Configuration precedence:
    /// </para>
    /// <list type="number">
    ///   <item>Named options are resolved and applied first</item>
    ///   <item>If no named options exist, default options are used</item>
    ///   <item>Options are passed to either named factories or the default factory</item>
    ///   <item>Scheduler chains are applied to the resulting task flow</item>
    /// </list>
    /// </remarks>
    public interface INamedConfigureTaskFlowOptions
    {
        /// <summary>
        /// Gets the name that identifies this options configuration.
        /// </summary>
        /// <value>
        /// A string that uniquely identifies this options configuration within the dependency injection container.
        /// This name is used to resolve which options should be used when creating named task flow instances.
        /// </value>
        /// <remarks>
        /// <para>
        /// The name serves as the key for options resolution in the dependency injection system.
        /// When <see cref="ITaskFlowFactory.CreateTaskFlow(string?)"/> is called with a specific name,
        /// the factory system searches for a registered <see cref="INamedConfigureTaskFlowOptions"/> with a
        /// matching <see cref="Name"/> property.
        /// </para>
        /// <para>
        /// Name matching considerations:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Case sensitivity</strong> - Names are typically case-sensitive</item>
        ///   <item><strong>Uniqueness</strong> - Each options configuration should have a unique name within the container</item>
        ///   <item><strong>Stability</strong> - The name should remain constant throughout the configuration's lifetime</item>
        ///   <item><strong>Coordination</strong> - Should match names used in factories and scheduler chains for consistency</item>
        /// </list>
        /// <para>
        /// If multiple options configurations are registered with the same name, the behavior depends on the
        /// dependency injection container implementation, but typically the last registered configuration
        /// will be used.
        /// </para>
        /// </remarks>
        string Name { get; }

        /// <summary>
        /// Creates and configures a <see cref="TaskFlowOptions"/> instance for this named configuration.
        /// </summary>
        /// <returns>A <see cref="TaskFlowOptions"/> instance configured according to this configuration's requirements.</returns>
        /// <remarks>
        /// <para>
        /// This method is responsible for creating and configuring the options that will be used to
        /// create task flow instances with the associated name. The method can access dependency injection
        /// services, configuration sources, or any other resources needed to determine the appropriate
        /// configuration.
        /// </para>
        /// <para>
        /// Configuration guidelines:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Consistency</strong> - Should return consistent options for the same conditions</item>
        ///   <item><strong>Validation</strong> - Should ensure returned options are valid and usable</item>
        ///   <item><strong>Performance</strong> - Should be efficient as it may be called multiple times</item>
        ///   <item><strong>Thread safety</strong> - Should be safe for concurrent access</item>
        /// </list>
        /// <para>
        /// The method can:
        /// </para>
        /// <list type="bullet">
        ///   <item>Read from configuration files or environment variables</item>
        ///   <item>Access other dependency injection services</item>
        ///   <item>Compute options based on runtime conditions</item>
        ///   <item>Provide different options for different environments</item>
        ///   <item>Apply business logic to determine appropriate settings</item>
        /// </list>
        /// <para>
        /// The returned options should be a new instance rather than a shared static instance to
        /// avoid potential modification issues, unless the options are truly immutable.
        /// </para>
        /// </remarks>
        TaskFlowOptions Configure();
    }
}