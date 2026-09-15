// Re-export the shared protocol types so app code imports from one
// place. The actual source of truth lives in shared/protocol/types.ts
// (shared with the C# agent's Protocol.cs — see PROTOCOL.md).
export * from "../../../shared/protocol/types";
