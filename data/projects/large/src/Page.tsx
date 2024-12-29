import * as React from 'react';
import * as components from './components';

export default () => (
  <div className="additional-content">
    <h2>I am additional content</h2>
    <p>Loaded from a separate bundle, just to be transferred when needed!</p>
    {Object.entries(components).map(([key, value]) => (
      <div key={key}>{`${key}: ${typeof value}`}</div>
    ))}
    <p>I would love to help you ...</p>
  </div>
);
