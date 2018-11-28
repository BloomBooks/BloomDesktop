// This declaration allows JSON data to be imported by TypeScript, albeit in an untyped way.
declare module "*.json" {
    const value: any;
    export default value;
}
