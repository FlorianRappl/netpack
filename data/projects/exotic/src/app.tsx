import * as React from "react";
import { render } from "react-dom";
import info from "./info.codegen";

const App = () => {
  React.useEffect(info, []);

  return (
    <div className="main-content">
      <header id="header">Some more exotic stuff</header>
      <div id="menu">
        <a href="/other">Some link</a>
      </div>
      <div className="post">
        This is a post. <a href="https://www.google.com">Google it</a>
      </div>
    </div>
  );
};

render(<App />, document.querySelector("#app"));
