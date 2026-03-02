## PESemantic Tree 

Here's the implementation of the semantic tree construction process and navigation.
This piece of code takes raw bytes, creates a root chain, then calls various parsers. The output is a clear hierarchy. We map the data (i.e., the fields) and calculate addresses (i.e., which field is located at a particular address).
This can be called file decomposition and decomposition into atomic fields.

and the Hex rendering file already contains the logic of jumping and navigation, that is, when you click on an address from the hierarchy, it will instantly jump by byte from the address

---

Sample:
[PEMapper.cs](/EUVA.Core/Parsers/PEMapper.cs)