export interface ComponentRenderRequest<TProps = Record<string, unknown>> {
    descriptor: {
        modulePath: string;
        exportName?: string;
    };
    props?: TProps;
}

export type ComponentRegistryEntry<TProps = Record<string, unknown>> =
    () => ComponentRenderRequest<TProps>;

export interface IBloomComponentConfig<TProps = Record<string, unknown>> {
    defaultProps: TProps;
    modulePath: string;
    exportName?: string;
}
