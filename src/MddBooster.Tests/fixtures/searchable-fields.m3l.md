# Namespace: test.search

Orders whose free-text search is meant for their number and customer name only.

## SearchOrder
- id: identifier @pk @generated
- order_number: string(30) @searchable
- customer_name: string(100) @searchable
- memo: text?
- share_token: string(64)?
- seq: integer

## SearchNote
- id: identifier @pk @generated
- body: text
