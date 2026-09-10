using System;
using System.Collections.Generic;
namespace NfsMwRemaster.Diagnostics
{
    public enum DiagnosticCommandContext { Sandbox, LiveProfile }
    public interface IDiagnosticCommand
    {
        string Id {get;}
        string SideEffects {get;}
        bool Reversible {get;}
        bool RequiresConfirmation {get;}
        bool Validate(double value);
        void Execute(double value);
    }
    /// <summary>Separate from read-only providers. Shipping builds fail closed regardless of caller flags.</summary>
    public sealed class DiagnosticCommands
    {
        private readonly Dictionary<string,IDiagnosticCommand> commands=new Dictionary<string,IDiagnosticCommand>();
        public IEnumerable<IDiagnosticCommand> Commands=>commands.Values;
        public void Register(IDiagnosticCommand command)
        {if(command==null || string.IsNullOrWhiteSpace(command.Id)||commands.Count>=32)throw new ArgumentException();commands.Add(command.Id,command);}
        public bool TryExecute(string id,double value,DiagnosticCommandContext context,bool confirmed,bool authorized,DiagnosticHub audit,DiagnosticClock clock)
        {
            bool ok=false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(authorized && context==DiagnosticCommandContext.Sandbox && commands.TryGetValue(id,out var cmd) &&
                (!cmd.RequiresConfirmation||confirmed) && double.IsFinite(value))
            {try{if(cmd.Validate(value)){cmd.Execute(value);ok=true;}}catch(Exception){ok=false;}}
#endif
            audit?.Publish(new DiagnosticEvent{clock=clock,provider="commands",code=ok?"command.succeeded":"command.denied-or-failed",severity=ok?0:2});return ok;
        }
    }
}
