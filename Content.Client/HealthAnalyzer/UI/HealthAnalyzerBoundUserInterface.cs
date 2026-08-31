using Content.Shared.MedicalScanner;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.HealthAnalyzer.UI
{
    [UsedImplicitly]
    public sealed class HealthAnalyzerBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private HealthAnalyzerWindow? _window;

        private HealthAnalyzerScannedUserMessage? _latestMessage;

        public HealthAnalyzerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _window = this.CreateWindow<HealthAnalyzerWindow>();

            _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

            if (_latestMessage != null)
                _window.Populate(_latestMessage);
        }

        protected override void ReceiveMessage(BoundUserInterfaceMessage message)
        {
            if (message is not HealthAnalyzerScannedUserMessage cast)
                return;

            _latestMessage = cast;
            _window?.Populate(cast);
        }
    }
}
