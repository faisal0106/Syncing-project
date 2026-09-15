interface Props {
  onDismiss: () => void;
}

const STEPS = [
  <>Ensure the <strong>MultiAudio Agent</strong> is running on this PC</>,
  <>Select two or more <strong>audio outputs</strong> from the device list</>,
  <>Connect Bluetooth devices in Windows first, then select their active audio endpoints here</>,
  <>Press <strong>Start Session</strong> to create a synchronized session</>,
  <>Hit <strong>Play</strong> to begin multi-device playback</>,
];

export function OnboardingOverlay({ onDismiss }: Props) {
  return (
    <div className="modal-overlay" onClick={onDismiss}>
      <div className="modal panel" onClick={(e) => e.stopPropagation()}>
        <div className="modal__header">
          <h2 className="modal__title">Welcome to MultiAudio</h2>
        </div>

        <p style={{ color: "var(--text-dim)", fontSize: "0.9rem", margin: "0 0 20px", lineHeight: 1.5 }}>
          Play the same audio through multiple Bluetooth devices simultaneously.
        </p>

        <div className="onboarding-steps">
          {STEPS.map((step, i) => (
            <div className="onboarding-step" key={i}>
              <span className="onboarding-step__num">{i + 1}</span>
              <span className="onboarding-step__text">{step}</span>
            </div>
          ))}
        </div>

        <button className="button-primary" onClick={onDismiss}>
          Get Started
        </button>
      </div>
    </div>
  );
}
