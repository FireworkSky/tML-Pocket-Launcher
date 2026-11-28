export function LaunchEmblem() {
  return (
    <div className="game-launch-emblem" aria-hidden="true">
      <div className="game-launch-orbit outer" />
      <div className="game-launch-orbit inner" />
      <div className="game-launch-disc">
        <img src="/tmodloader.png" alt="" />
      </div>
      <i className="game-launch-pixel one" />
      <i className="game-launch-pixel two" />
    </div>
  );
}
