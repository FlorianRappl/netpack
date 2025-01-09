import data from "./data.json";

function fn() {
  console.log("fn is called.", data);

  import("./meta.json").then((res) => console.log("Result =", res));
}

export default fn;
