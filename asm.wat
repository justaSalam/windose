(module

    (import "windose" "core.test"
        (func $core_test (param i32))
    )

    (func (export "main")
        i32.const 123
        call $core_test
    )
)