import * as React from "react";
import { BrowserRouter as Router, Switch, Route } from "react-router-dom";

import Header from "./pages/Header";
import Home from "./pages/Home";
import About from "./pages/About";
import Contact from "./pages/Contact";

const App = () => (
  <Router>
    <Header />
    <Switch>
      <Route exact path="/">
        <Home />
      </Route>

      <Route exact path="/about">
        <About />
      </Route>

      <Route exact path="/contact">
        <Contact />
      </Route>
    </Switch>
  </Router>
);

export default App;
