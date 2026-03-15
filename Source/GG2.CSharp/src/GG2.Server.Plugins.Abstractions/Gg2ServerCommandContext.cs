namespace GG2.Server.Plugins;

public readonly record struct Gg2ServerCommandContext(
    IGg2ServerReadOnlyState ServerState,
    IGg2ServerAdminOperations AdminOperations);
